import argparse
import ast
import json
import queue
import re
import subprocess
import sys
import threading
import time
from itertools import combinations
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
    "WHITE", "PINK", "CYAN", "YELLOW", "BLUE", "RED", "GREEN",
    "ORANGE", "LIME", "VIOLET", "PURPLE", "GRAY", "GOLD",
    "TURQUOISE", "AQUA", "TEAL", "MAGENTA", "LEMON",
)

SLOT_SETS = {
    "progressive": {
        1: (1,),
        2: (1, 2),
        4: (1, 2, 3, 4),
        6: (1, 2, 3, 4, 5, 6),
        8: (1, 2, 3, 4, 5, 6, 7, 8),
    },
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


def color_order(colors):
    order = {color: index for index, color in enumerate(PREFERRED_ORDER)}
    return sorted(colors, key=lambda color: (order.get(color, 999), color))


def parse_csv(value):
    return [part.strip().upper() for part in str(value or "").split(",") if part.strip()]


def parse_sizes(value):
    sizes = []
    for part in str(value or "").replace(";", ",").split(","):
        part = part.strip()
        if part:
            sizes.append(int(part))
    return sizes


def parse_combo_specs(value):
    specs = []
    for part in str(value or "").replace(";", ",").split(","):
        part = part.strip()
        if not part:
            continue
        left, sep, right = part.partition("+")
        if sep != "+":
            raise argparse.ArgumentTypeError("combo specs must look like 2+6,4+4")
        a = int(left.strip())
        b = int(right.strip())
        if a < 1 or b < 1 or a + b > 8:
            raise argparse.ArgumentTypeError("combo counts must be positive and use at most 8 slots")
        specs.append((a, b))
    if not specs:
        raise argparse.ArgumentTypeError("at least one combo spec is required")
    return specs


def pct_key(values):
    return ",".join(str(v) for v in values)


def active_intensity(values):
    return tuple(100 - int(v) for v in values)


def load_score(values, count):
    return sum(active_intensity(values)) * count


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
        if ("access" in lowered or "acc" in lowered or "refus" in lowered) and "com" in lowered:
            access_denied_count += 1
            if access_denied_count >= 3:
                return "access-denied"
    return "timeout"


def send(process, command):
    print(f"TX {command}")
    process.stdin.write(command + "\n")
    process.stdin.flush()


def kill_existing_sender(sender_path):
    if sys.platform != "win32":
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


def answered_keys(path):
    keys = set()
    for item in load_jsonl(path):
        if item.get("ok") in (True, False):
            keys.add(item.get("key"))
    return keys


def batch_command(commands):
    return "BATCH " + ";".join(commands)


def slot_command(slot, color, command):
    return f"{command} {slot} {color}"


def all_slots_color_commands(color, command):
    return [slot_command(slot, color, command) for slot in range(1, 9)]


def single_color_lot_commands(slots, color, command):
    active = set(slots)
    return [
        slot_command(slot, color if slot in active else "BLACK", command)
        for slot in range(1, 9)
    ]


def combo_slots(left_count, right_count):
    left = tuple(range(1, left_count + 1))
    right = tuple(range(9 - right_count, 9))
    if set(left) & set(right):
        right = tuple(range(left_count + 1, left_count + right_count + 1))
    return left, right


def combo_commands(left_slots, left_color, right_slots, right_color, command):
    left = set(left_slots)
    right = set(right_slots)
    commands = []
    for slot in range(1, 9):
        if slot in left:
            color = left_color
        elif slot in right:
            color = right_color
        else:
            color = "BLACK"
        commands.append(slot_command(slot, color, command))
    return commands


def build_tests(args, color_map):
    colors = parse_csv(args.colors) if args.colors else color_order(color_map.keys())
    colors = [color for color in colors if color != "BLACK"]
    unknown = [color for color in colors if color not in color_map]
    if unknown:
        raise ValueError(f"unknown colors: {', '.join(sorted(set(unknown)))}")

    suites = {part.strip().lower() for part in args.suite.split(",") if part.strip()}
    tests = []

    if "full" in suites:
        for color in colors:
            values = color_map[color]
            tests.append({
                "key": f"full:{color}",
                "phase": "full",
                "title": f"FULL {color} sur B1-B8",
                "expected": f"Tous les boutons B1-B8 doivent etre {color}, uniforme et stable.",
                "colors": [color],
                "pct": {color: pct_key(values)},
                "load_score": load_score(values, 8),
                "commands": all_slots_color_commands(color, args.command),
            })

    if "lot" in suites:
        for slot_set_name in args.slot_sets:
            slot_sets = SLOT_SETS[slot_set_name]
            for color in colors:
                values = color_map[color]
                for count in args.sizes:
                    if count not in slot_sets:
                        continue
                    slots = slot_sets[count]
                    tests.append({
                        "key": f"lot:{slot_set_name}:{color}:{count}",
                        "phase": "lot",
                        "title": f"LOT {color} sur {count} bouton(s) [{slot_set_name}]",
                        "expected": f"Slots {','.join(str(s) for s in slots)} = {color}; autres slots eteints.",
                        "colors": [color],
                        "slot_set": slot_set_name,
                        "count": count,
                        "slots": list(slots),
                        "pct": {color: pct_key(values)},
                        "load_score": load_score(values, count),
                        "commands": single_color_lot_commands(slots, color, args.command),
                    })

    if "combo" in suites:
        for left_color, right_color in combinations(colors, 2):
            left_values = color_map[left_color]
            right_values = color_map[right_color]
            for left_count, right_count in args.combo_specs:
                left_slots, right_slots = combo_slots(left_count, right_count)
                tests.append({
                    "key": f"combo:{left_color}:{right_color}:{left_count}+{right_count}",
                    "phase": "combo",
                    "title": f"COMBO {left_color} + {right_color} ({left_count}+{right_count})",
                    "expected": (
                        f"Slots {','.join(str(s) for s in left_slots)} = {left_color}; "
                        f"slots {','.join(str(s) for s in right_slots)} = {right_color}; autres eteints."
                    ),
                    "colors": [left_color, right_color],
                    "left_color": left_color,
                    "right_color": right_color,
                    "left_count": left_count,
                    "right_count": right_count,
                    "left_slots": list(left_slots),
                    "right_slots": list(right_slots),
                    "pct": {
                        left_color: pct_key(left_values),
                        right_color: pct_key(right_values),
                    },
                    "load_score": load_score(left_values, left_count) + load_score(right_values, right_count),
                    "commands": combo_commands(left_slots, left_color, right_slots, right_color, args.command),
                })

    done = answered_keys(Path(args.results)) if not args.rerun else set()
    tests = [test for test in tests if test["key"] not in done]
    tests.sort(key=lambda item: (item["phase"], item["load_score"], item["key"]))
    if args.limit > 0:
        tests = tests[:args.limit]
    return tests


def ask_result():
    while True:
        answer = input("Resultat ? o/n, Enter=skip, q=quit > ").strip().lower()
        if answer in ("q", "quit", "exit"):
            return "quit", ""
        if answer in ("", "skip", "s"):
            return None, ""
        if answer in ("o", "oui", "ok", "y", "yes"):
            note = input("Note optionnelle, Enter=rien > ").strip()
            return True, note
        if answer in ("n", "non", "no"):
            note = input("Qu'est-ce que tu vois ? ex: rose, slot 6 off, pas uniforme > ").strip()
            return False, note
        print("Reponds o, n, Enter, ou q.")


def print_summary(path):
    items = [item for item in load_jsonl(path) if item.get("ok") in (True, False)]
    if not items:
        print(f"No results in {path}.")
        return
    ok = sum(1 for item in items if item.get("ok") is True)
    ko = sum(1 for item in items if item.get("ok") is False)
    print(f"Results: {path}")
    print(f"OK={ok} NO={ko} total={len(items)}")
    for item in items:
        verdict = "OK" if item.get("ok") else "NO"
        print(f"{verdict:<2} {item.get('phase'):<5} load={item.get('load_score'):<4} {item.get('title')} {item.get('note') or ''}")


def main():
    parser = argparse.ArgumentParser(
        description="Interactive color/power sweep after LED power wiring changes."
    )
    parser.add_argument("--sender", default="PicoCommandSender.exe")
    parser.add_argument("--ini", default="PicoCommandSender.ini")
    parser.add_argument("--sender-id", default="P1")
    parser.add_argument("--fw-main", default="fw/main.py")
    parser.add_argument("--results", default="state/color_power_sweep_results.jsonl")
    parser.add_argument("--suite", default="full,lot,combo", help="Comma list: full,lot,combo")
    parser.add_argument("--colors", default="", help="Comma list. Default: all firmware colors except BLACK.")
    parser.add_argument("--sizes", type=parse_sizes, default=parse_sizes("1,2,4,6,8"))
    parser.add_argument("--slot-sets", type=parse_csv, default=parse_csv("progressive,spread"))
    parser.add_argument("--combo-specs", type=parse_combo_specs, default=parse_combo_specs("2+6,4+4,6+2"))
    parser.add_argument("--command", choices=("SLOT", "SLOTPWM"), default="SLOTPWM")
    parser.add_argument("--send-mode", choices=("batch", "lines"), default="batch")
    parser.add_argument("--line-delay-ms", type=int, default=20)
    parser.add_argument("--settle-ms", type=int, default=350)
    parser.add_argument("--auto", action="store_true", help="Run without prompts, changing test every --interval-ms.")
    parser.add_argument("--interval-ms", type=int, default=1000, help="Delay between tests in --auto mode.")
    parser.add_argument("--log-auto", action="store_true", help="Append shown entries to results in --auto mode.")
    parser.add_argument("--ready-timeout", type=float, default=18.0)
    parser.add_argument("--include-start-select-load", action="store_true")
    parser.add_argument("--start-select-color", default="ORANGE")
    parser.add_argument("--kill-existing-sender", action="store_true")
    parser.add_argument("--rerun", action="store_true")
    parser.add_argument("--keep-last", action="store_true")
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--summary", action="store_true")
    args = parser.parse_args()

    results_path = Path(args.results)
    if args.summary:
        print_summary(results_path)
        return 0

    args.slot_sets = [name.lower() for name in args.slot_sets]
    for name in args.slot_sets:
        if name not in SLOT_SETS:
            print(f"ERROR unknown slot-set: {name}", file=sys.stderr)
            return 2

    color_map = load_color_map(args.fw_main)
    try:
        tests = build_tests(args, color_map)
    except ValueError as exc:
        print(f"ERROR {exc}", file=sys.stderr)
        return 2

    if not tests:
        print("No remaining tests. Use --rerun to replay.")
        return 0

    if args.kill_existing_sender:
        print("Killing existing PicoCommandSender processes before test...")
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
        ready = wait_ready(output_queue, args.ready_timeout)
        if ready == "access-denied":
            print("ERROR COM port access denied. Stop LedManager or use --kill-existing-sender.", file=sys.stderr)
            return 2
        if ready != "ready":
            print("ERROR sender did not become READY in time.", file=sys.stderr)
            return 2

        print("")
        print("Test couleurs x charge electrique.")
        print(f"Results: {results_path}")
        print(f"Suite: {args.suite}")
        print(f"Tests restants: {len(tests)}")
        if args.auto:
            print(f"Mode auto: aucun prompt, changement toutes les {args.interval_ms}ms.")
            print("Ctrl+C pour arreter quand tu as vu assez.")
        else:
            print("Reponds o si tout est stable/uniforme et correspond a l'attendu.")
            print("Reponds n si couleur fausse, intensite faible, slot eteint, flicker, ou non-uniformite.")
        print("")

        if args.include_start_select_load:
            send(process, f"SET START {args.start_select_color.upper()}")
            send(process, f"SET SELECT {args.start_select_color.upper()}")
            time.sleep(0.15)

        for index, test in enumerate(tests, start=1):
            print(f"[{index}/{len(tests)}] {test['title']}")
            print(f"Attendu: {test['expected']}")
            print(f"Pct: {test['pct']} | load_score={test['load_score']}")
            if args.send_mode == "batch":
                send(process, batch_command(test["commands"]))
            else:
                for command in test["commands"]:
                    send(process, command)
                    if args.line_delay_ms > 0:
                        time.sleep(args.line_delay_ms / 1000.0)
            time.sleep(max(0, args.settle_ms) / 1000.0)

            if args.auto:
                if args.log_auto:
                    append_jsonl(results_path, {
                        **{key: value for key, value in test.items() if key != "commands"},
                        "shown": True,
                        "command_kind": args.command,
                        "send_mode": args.send_mode,
                        "include_start_select_load": args.include_start_select_load,
                        "commands": test["commands"],
                        "command": batch_command(test["commands"]),
                        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                    })
                wait_ms = max(0, args.interval_ms - max(0, args.settle_ms))
                if wait_ms > 0:
                    time.sleep(wait_ms / 1000.0)
                print("")
                continue

            ok, note = ask_result()
            if ok == "quit":
                break
            if ok is None:
                print("Skip.")
                print("")
                continue

            append_jsonl(results_path, {
                **{key: value for key, value in test.items() if key != "commands"},
                "ok": ok,
                "note": note,
                "command_kind": args.command,
                "send_mode": args.send_mode,
                "include_start_select_load": args.include_start_select_load,
                "commands": test["commands"],
                "command": batch_command(test["commands"]),
                "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
            })
            print("Saved.")
            print("")

        return 0
    finally:
        if not args.keep_last:
            try:
                send(process, "ALL BLACK")
                send(process, "SET START BLACK")
                send(process, "SET SELECT BLACK")
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
