import argparse
import ast
import json
import queue
import re
import subprocess
import sys
import threading
import time
from pathlib import Path


DEFAULT_COLOR_MAP = {
    # GPIO direct extinction percentages from fw/main.py:
    # 0 = channel fully lit, 100 = channel off.
    "WHITE": (0, 0, 0),
    "PINK": (100, 0, 0),
    "CYAN": (0, 100, 0),
    "YELLOW": (0, 0, 100),
    "BLUE": (100, 100, 0),
    "RED": (100, 0, 100),
    "GREEN": (0, 100, 100),
    "BLACK": (100, 100, 100),
    "ORANGE": (50, 0, 100),
    "LIME": (25, 100, 100),
    "VIOLET": (100, 75, 0),
    "PURPLE": (75, 25, 0),
    "GRAY": (50, 50, 50),
    "GOLD": (75, 25, 100),
    "TURQUOISE": (25, 100, 0),
    "AQUA": (50, 100, 0),
    "TEAL": (25, 75, 0),
    "MAGENTA": (100, 50, 0),
    "LEMON": (0, 25, 100),
}

PREFERRED_ORDER = (
    "WHITE", "PINK", "CYAN", "YELLOW", "BLUE", "RED", "GREEN", "BLACK",
    "ORANGE", "LIME", "VIOLET", "PURPLE", "GRAY", "GOLD", "TURQUOISE",
    "AQUA", "TEAL", "MAGENTA", "LEMON",
)

DEFAULT_TEST_COLORS = (
    # Enough signal to infer the load rule without replaying every known color.
    "ORANGE", "GOLD", "LIME", "VIOLET", "PURPLE", "GRAY", "TURQUOISE",
    "AQUA", "MAGENTA", "LEMON", "WHITE", "YELLOW", "RED", "GREEN", "BLUE",
)

DEFAULT_COMBO_SHADES = (
    "ORANGE", "GOLD", "LIME", "VIOLET", "PURPLE", "GRAY",
    "TURQUOISE", "AQUA", "MAGENTA", "LEMON",
)

DEFAULT_COMBO_BASES = (
    "WHITE", "YELLOW", "RED", "GREEN", "BLUE",
)

SLOT_SETS = {
    # Progressive sets are closest to real panel batches.
    "progressive": {
        1: (1,),
        2: (1, 2),
        4: (1, 2, 3, 4),
        6: (1, 2, 3, 4, 5, 6),
        8: (1, 2, 3, 4, 5, 6, 7, 8),
    },
    # Spread sets catch weak wiring/power effects without too many extra tests.
    "spread": {
        1: (8,),
        2: (1, 8),
        4: (1, 3, 6, 8),
        6: (1, 2, 3, 6, 7, 8),
        8: (1, 2, 3, 4, 5, 6, 7, 8),
    },
}


def parse_color_dict_from_main(text, name):
    match = re.search(rf"^{name}\s*=\s*(\{{.*?^\}})", text, re.MULTILINE | re.DOTALL)
    if not match:
        return {}

    try:
        return {
            str(key).upper(): tuple(int(v) for v in value)
            for key, value in ast.literal_eval(match.group(1)).items()
        }
    except Exception as exc:
        print(f"WARNING could not parse {name} from fw/main.py: {exc}", file=sys.stderr)
        return {}


def load_color_map(path):
    path = Path(path)
    if not path.exists():
        print(f"WARNING firmware file not found, using embedded colors: {path}", file=sys.stderr)
        return DEFAULT_COLOR_MAP

    text = path.read_text(encoding="utf-8", errors="replace")
    loaded = {}
    loaded.update(parse_color_dict_from_main(text, "PRIMARY"))
    loaded.update(parse_color_dict_from_main(text, "SHADES"))
    if not loaded:
        print(f"WARNING no PRIMARY/SHADES parsed from {path}, using embedded colors.", file=sys.stderr)
        return DEFAULT_COLOR_MAP
    return loaded


def read_lines(stream, prefix, output_queue):
    for line in iter(stream.readline, ""):
        text = line.rstrip()
        if text:
            print(f"{prefix} {text}")
            output_queue.put(text)


def wait_ready(output_queue, timeout):
    deadline = time.time() + timeout
    access_denied_count = 0
    while time.time() < deadline:
        try:
            line = output_queue.get(timeout=0.1)
        except queue.Empty:
            continue
        if "READY sender=" in line:
            return "ready"
        lowered = line.lower()
        if "access" in lowered or "acc" in lowered or "refus" in lowered:
            if "com" in lowered:
                access_denied_count += 1
                if access_denied_count >= 3:
                    return "access-denied"
    return "timeout"


def send(process, command):
    print(f"TX {command}")
    process.stdin.write(command + "\n")
    process.stdin.flush()


def send_test_commands(process, commands, line_delay_ms):
    for command in commands:
        send(process, command)
        if line_delay_ms > 0:
            time.sleep(line_delay_ms / 1000.0)


def append_jsonl(path, item):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(item, ensure_ascii=False, sort_keys=True) + "\n")


def load_jsonl(path):
    if not path.exists():
        return []
    items = []
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                items.append(json.loads(line))
            except json.JSONDecodeError:
                print(f"WARNING ignored invalid JSONL line: {line}", file=sys.stderr)
    return items


def kill_existing_sender(sender_path):
    if sys.platform != "win32":
        print("WARNING --kill-existing-sender is only implemented on Windows.", file=sys.stderr)
        return

    sender_name = Path(sender_path).name
    if not sender_name.lower().endswith(".exe"):
        sender_name += ".exe"

    subprocess.run(
        ["taskkill", "/F", "/IM", sender_name],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    )


def parse_csv(value):
    return [part.strip().upper() for part in str(value or "").split(",") if part.strip()]


def parse_sizes(value):
    sizes = []
    for part in str(value or "").replace(";", ",").split(","):
        part = part.strip()
        if not part:
            continue
        sizes.append(int(part))
    return sizes


def parse_combo_specs(value):
    specs = []
    for part in str(value or "").replace(";", ",").split(","):
        part = part.strip().lower()
        if not part:
            continue
        if "+" not in part:
            raise argparse.ArgumentTypeError("combo specs must look like 2+6,4+4")
        left, right = part.split("+", 1)
        try:
            shade_count = int(left.strip())
            base_count = int(right.strip())
        except ValueError as exc:
            raise argparse.ArgumentTypeError("combo counts must be integers") from exc
        if shade_count < 1 or base_count < 1 or shade_count + base_count > 8:
            raise argparse.ArgumentTypeError("combo counts must be positive and use at most 8 slots")
        specs.append((shade_count, base_count))
    if not specs:
        raise argparse.ArgumentTypeError("at least one combo spec is required")
    return specs


def pct_key(values):
    return ",".join(str(v) for v in values)


def active_intensity(values):
    # Firmware pct values are extinction percentages.
    return tuple(100 - int(v) for v in values)


def load_score(values, count):
    return sum(active_intensity(values)) * count


def build_commands(slots, color, command):
    active = {int(slot) for slot in slots}
    commands = []
    for slot in range(1, 9):
        slot_color = color if slot in active else "BLACK"
        commands.append(f"{command} {slot} {slot_color}")
    return commands


def batch_command(commands):
    return "BATCH " + ";".join(commands)


def combo_slots(shade_count, base_count):
    shade_slots = tuple(range(1, shade_count + 1))
    base_start = 9 - base_count
    base_slots = tuple(range(base_start, 9))
    overlap = set(shade_slots) & set(base_slots)
    if overlap:
        base_slots = tuple(range(shade_count + 1, shade_count + base_count + 1))
    return shade_slots, base_slots


def build_combo_commands(shade_slots, shade_color, base_slots, base_color, command):
    shade_active = {int(slot) for slot in shade_slots}
    base_active = {int(slot) for slot in base_slots}
    commands = []
    for slot in range(1, 9):
        if slot in shade_active:
            slot_color = shade_color
        elif slot in base_active:
            slot_color = base_color
        else:
            slot_color = "BLACK"
        commands.append(f"{command} {slot} {slot_color}")
    return commands


def answered_keys(path):
    keys = set()
    for item in load_jsonl(path):
        if item.get("phase") != "lot-rule":
            continue
        if item.get("ok") in (True, False):
            key = (item.get("color"), int(item.get("count") or 0), item.get("slot_set"))
            keys.add(key)
    return keys


def answered_combo_keys(path):
    keys = set()
    for item in load_jsonl(path):
        if item.get("phase") != "combo-rule":
            continue
        if item.get("ok") in (True, False):
            keys.add((
                item.get("combo_type") or "shade-primary",
                item.get("shade_color"),
                item.get("base_color"),
                int(item.get("shade_count") or 0),
                int(item.get("base_count") or 0),
            ))
    return keys


def ask_result():
    while True:
        answer = input("Resultat ? o/n, Enter=skip, q=quit > ").strip()
        lower = answer.lower()
        if lower in ("q", "quit", "exit"):
            return "quit", ""
        if lower in ("", "skip", "s"):
            return None, ""
        if lower in ("o", "oui", "ok", "y", "yes"):
            note = input("Note optionnelle, Enter=rien > ").strip()
            return True, note
        if lower in ("n", "non", "no"):
            note = input("Qu'est-ce que tu vois ? ex: rouge, rose partiel, slot 6 off > ").strip()
            return False, note
        print("Reponds o, n, Enter, ou q.")


def main():
    parser = argparse.ArgumentParser(
        description="Interactive lot-size color stability test for Pico GPIO RGB panels."
    )
    parser.add_argument("--mode", choices=("lot", "combo"), default="lot")
    parser.add_argument("--sender", default="PicoCommandSender.exe")
    parser.add_argument("--ini", default="PicoCommandSender.ini")
    parser.add_argument("--sender-id", default="P1")
    parser.add_argument("--fw-main", default="fw/main.py")
    parser.add_argument("--results", default="state/color_lot_rule_results.jsonl")
    parser.add_argument("--ready-timeout", type=float, default=18.0)
    parser.add_argument("--settle-ms", type=int, default=300)
    parser.add_argument(
        "--kill-existing-sender",
        action="store_true",
        help="Kill existing PicoCommandSender.exe processes before starting the calibration daemon.",
    )
    parser.add_argument("--colors", default=",".join(DEFAULT_TEST_COLORS))
    parser.add_argument("--sizes", type=parse_sizes, default=parse_sizes("1,2,4,6,8"))
    parser.add_argument("--combo-shades", default=",".join(DEFAULT_COMBO_SHADES))
    parser.add_argument("--combo-bases", default=",".join(DEFAULT_COMBO_BASES))
    parser.add_argument("--combo-specs", type=parse_combo_specs, default=parse_combo_specs("2+6,4+4,6+2"))
    parser.add_argument(
        "--combo-type",
        choices=("shade-primary", "shade-shade", "both"),
        default="both",
        help="Combination family tested by --mode combo.",
    )
    parser.add_argument("--slot-set", choices=sorted(SLOT_SETS.keys()), default="progressive")
    parser.add_argument("--command", choices=("SLOT", "SLOTPWM"), default="SLOTPWM")
    parser.add_argument(
        "--send-mode",
        choices=("lines", "batch"),
        default="lines",
        help="Use separate serial lines by default; batch is faster but can stress the Pico/bridge.",
    )
    parser.add_argument("--line-delay-ms", type=int, default=25)
    parser.add_argument("--rerun", action="store_true")
    parser.add_argument("--keep-last", action="store_true")
    parser.add_argument("--summary", action="store_true", help="Print current results and exit.")
    args = parser.parse_args()

    results_path = Path(args.results)
    color_map = load_color_map(args.fw_main)

    colors = parse_csv(args.colors)
    combo_shades = parse_csv(args.combo_shades)
    combo_bases = parse_csv(args.combo_bases)
    unknown = [color for color in colors if color not in color_map]
    unknown += [color for color in combo_shades if color not in color_map]
    unknown += [color for color in combo_bases if color not in color_map]
    if unknown:
        print(f"ERROR unknown colors in firmware map: {', '.join(sorted(set(unknown)))}", file=sys.stderr)
        return 2

    if args.summary:
        items = [
            item for item in load_jsonl(results_path)
            if item.get("phase") in ("lot-rule", "combo-rule")
        ]
        if not items:
            print(f"No rule results in {results_path}.")
            return 0
        for item in sorted(items, key=lambda x: (str(x.get("phase")), str(x.get("color") or x.get("shade_color")), int(x.get("count") or x.get("shade_count") or 0), str(x.get("base_color") or ""))):
            verdict = "OK" if item.get("ok") is True else "NO" if item.get("ok") is False else "SKIP"
            if item.get("phase") == "combo-rule":
                print(
                    f"COMBO {item.get('combo_type') or 'shade-primary':<13} "
                    f"{item.get('shade_color'):<10}+{item.get('base_color'):<10} "
                    f"{item.get('shade_count')}+{item.get('base_count')} {verdict:<4} "
                    f"shade_slots={item.get('shade_slots')} base_slots={item.get('base_slots')} "
                    f"load={item.get('load_score')} {item.get('note') or ''}"
                )
            else:
                print(f"LOT   {item.get('color'):<10} count={item.get('count')} slots={item.get('slots')} {verdict:<4} pct={item.get('pct')} load={item.get('load_score')} {item.get('note') or ''}")
        return 0

    tests = []
    if args.mode == "combo":
        done = answered_combo_keys(results_path) if not args.rerun else set()
        combo_pairs = []
        if args.combo_type in ("shade-primary", "both"):
            combo_pairs.extend(
                ("shade-primary", shade_color, base_color)
                for shade_color in combo_shades
                for base_color in combo_bases
                if shade_color != base_color
            )
        if args.combo_type in ("shade-shade", "both"):
            for left_index, shade_color in enumerate(combo_shades):
                for base_color in combo_shades[left_index + 1:]:
                    if shade_color != base_color:
                        combo_pairs.append(("shade-shade", shade_color, base_color))

        for combo_type, shade_color, base_color in combo_pairs:
            for shade_count, base_count in args.combo_specs:
                key = (combo_type, shade_color, base_color, shade_count, base_count)
                if key in done:
                    continue
                shade_slots, base_slots = combo_slots(shade_count, base_count)
                shade_values = color_map[shade_color]
                base_values = color_map[base_color]
                tests.append({
                    "phase": "combo-rule",
                    "combo_type": combo_type,
                    "shade_color": shade_color,
                    "base_color": base_color,
                    "shade_count": shade_count,
                    "base_count": base_count,
                    "shade_slots": shade_slots,
                    "base_slots": base_slots,
                    "shade_pct": shade_values,
                    "base_pct": base_values,
                    "shade_intensity": active_intensity(shade_values),
                    "base_intensity": active_intensity(base_values),
                    "load_score": load_score(shade_values, shade_count) + load_score(base_values, base_count),
                    "commands": build_combo_commands(shade_slots, shade_color, base_slots, base_color, args.command),
                })
        tests.sort(key=lambda item: (
            0 if item["combo_type"] == "shade-primary" else 1,
            PREFERRED_ORDER.index(item["shade_color"]) if item["shade_color"] in PREFERRED_ORDER else 999,
            PREFERRED_ORDER.index(item["base_color"]) if item["base_color"] in PREFERRED_ORDER else 999,
            item["shade_count"],
            item["base_count"],
        ))
    else:
        slot_sets = SLOT_SETS[args.slot_set]
        done = answered_keys(results_path) if not args.rerun else set()
        for color in colors:
            for count in args.sizes:
                if count not in slot_sets:
                    print(f"WARNING ignored unsupported size {count} for slot-set {args.slot_set}.", file=sys.stderr)
                    continue
                key = (color, count, args.slot_set)
                if key in done:
                    continue
                values = color_map[color]
                slots = slot_sets[count]
                tests.append({
                    "phase": "lot-rule",
                    "color": color,
                    "count": count,
                    "slots": slots,
                    "pct": values,
                    "intensity": active_intensity(values),
                    "load_score": load_score(values, count),
                    "commands": build_commands(slots, color, args.command),
                })

        tests.sort(key=lambda item: (PREFERRED_ORDER.index(item["color"]) if item["color"] in PREFERRED_ORDER else 999, item["count"]))

    if not tests:
        print("No remaining tests. Use --rerun to replay.")
        return 0

    if args.kill_existing_sender:
        print("Killing existing PicoCommandSender processes before calibration...")
        kill_existing_sender(args.sender)
        time.sleep(0.5)

    output_queue = queue.Queue()
    process = subprocess.Popen(
        [args.sender, "daemon", "--ini", args.ini, "--sender", args.sender_id],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )

    threading.Thread(target=read_lines, args=(process.stdout, "OUT", output_queue), daemon=True).start()
    threading.Thread(target=read_lines, args=(process.stderr, "ERR", output_queue), daemon=True).start()

    try:
        print(f"Waiting for sender READY ({args.ready_timeout:.0f}s max)...")
        ready_status = wait_ready(output_queue, args.ready_timeout)
        if ready_status == "access-denied":
            print("")
            print("ERROR COM port access denied.", file=sys.stderr)
            print("COM3 is probably already used by LedManager or an old PicoCommandSender.", file=sys.stderr)
            print("Stop LedManager, or rerun this script with --kill-existing-sender.", file=sys.stderr)
            return 2
        if ready_status != "ready":
            print("ERROR sender did not become READY in time.", file=sys.stderr)
            return 2

        print("")
        if args.mode == "combo":
            print("Test de combinaisons shade + primaire / shade + shade pour deduire la regle couleur x charge.")
        else:
            print("Test par lots pour deduire la regle couleur x charge.")
        print(f"Results: {results_path}")
        if args.mode == "combo":
            print(f"Combo specs: {','.join(f'{a}+{b}' for a, b in args.combo_specs)}")
            print(f"Combo type: {args.combo_type}")
        else:
            print(f"Slot set: {args.slot_set}")
        print(f"Command: {args.command}")
        print(f"Send mode: {args.send_mode}")
        print(f"Tests restants: {len(tests)}")
        print("")
        print("Tu dois voir uniquement les slots annonces, dans les couleurs annoncees.")
        print("Si la couleur vire, si un slot reste off, ou si ce n'est pas uniforme: reponds n.")
        print("")

        for index, test in enumerate(tests, start=1):
            if test["phase"] == "combo-rule":
                shade_slots = ",".join(str(slot) for slot in test["shade_slots"])
                base_slots = ",".join(str(slot) for slot in test["base_slots"])
                print(
                    f"[{index}/{len(tests)}] {test['combo_type']}: {test['shade_color']} + {test['base_color']} "
                    f"({test['shade_count']}+{test['base_count']} boutons)"
                )
                print(
                    f"Attendu: slots {shade_slots} = {test['shade_color']} ; "
                    f"slots {base_slots} = {test['base_color']} ; autres eteints."
                )
                print(
                    f"Shade pct={pct_key(test['shade_pct'])} intensity={pct_key(test['shade_intensity'])} | "
                    f"Base pct={pct_key(test['base_pct'])} intensity={pct_key(test['base_intensity'])} | "
                    f"load_score={test['load_score']}"
                )
            else:
                color = test["color"]
                slots = ",".join(str(slot) for slot in test["slots"])
                pct = pct_key(test["pct"])
                intensity = pct_key(test["intensity"])
                print(f"[{index}/{len(tests)}] {color} sur {test['count']} bouton(s): slots {slots}")
                print(f"Attendu: slots {slots} = {color}, tous les autres slots eteints.")
                print(f"Pct extinction={pct} intensite={intensity} load_score={test['load_score']}")
            commands = test["commands"]
            if args.send_mode == "batch":
                send(process, batch_command(commands))
            else:
                send_test_commands(process, commands, max(0, args.line_delay_ms))
            time.sleep(max(0, args.settle_ms) / 1000.0)

            result, note = ask_result()
            if result == "quit":
                break
            if result is None:
                print("Skip.")
                print("")
                continue

            item = {
                "phase": test["phase"],
                "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                "load_score": test["load_score"],
                "command_kind": args.command,
                "send_mode": args.send_mode,
                "commands": test["commands"],
                "command": batch_command(test["commands"]),
                "ok": result,
                "note": note,
            }
            if test["phase"] == "combo-rule":
                item.update({
                    "combo_type": test["combo_type"],
                    "shade_color": test["shade_color"],
                    "base_color": test["base_color"],
                    "shade_count": test["shade_count"],
                    "base_count": test["base_count"],
                    "shade_slots": list(test["shade_slots"]),
                    "base_slots": list(test["base_slots"]),
                    "shade_pct": pct_key(test["shade_pct"]),
                    "base_pct": pct_key(test["base_pct"]),
                    "shade_intensity": pct_key(test["shade_intensity"]),
                    "base_intensity": pct_key(test["base_intensity"]),
                })
            else:
                item.update({
                    "color": test["color"],
                    "count": test["count"],
                    "slots": list(test["slots"]),
                    "slot_set": args.slot_set,
                    "pct": pct_key(test["pct"]),
                    "intensity": pct_key(test["intensity"]),
                })
            append_jsonl(results_path, item)
            print("Saved.")
            print("")

        return 0
    finally:
        if not args.keep_last:
            try:
                send(process, "ALL BLACK")
                time.sleep(0.2)
            except Exception:
                pass
        try:
            process.stdin.close()
        except Exception:
            pass
        try:
            process.terminate()
            process.wait(timeout=3)
        except Exception:
            try:
                process.kill()
            except Exception:
                pass


if __name__ == "__main__":
    raise SystemExit(main())
