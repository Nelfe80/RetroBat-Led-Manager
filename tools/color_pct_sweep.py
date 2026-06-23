import argparse
import ast
import itertools
import json
import queue
import re
import subprocess
import sys
import threading
import time
from pathlib import Path


FIRMWARE_COLOR_MAP = {
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

PREFERRED_COLOR_ORDER = (
    "WHITE", "PINK", "CYAN", "YELLOW", "BLUE", "RED", "GREEN", "BLACK",
    "ORANGE", "LIME", "VIOLET", "PURPLE", "GRAY", "GOLD", "TURQUOISE",
    "AQUA", "TEAL", "MAGENTA", "LEMON",
)

LOT_FILTERS = {
    "neutral": "White,Gray,Black",
    "yellow": "Yellow,Lemon",
    "warm": "Orange,Gold",
    "green": "Green,Lime",
    "aqua": "Cyan,Aqua,Turquoise,Turqoise,Teal",
    "blue": "Blue",
    "pink": "Pink,Red",
    "purple": "Magenta,Purple,Violet",
}


def parse_color_dict_from_main(text, name):
    match = re.search(rf"^{name}\s*=\s*(\{{.*?^\}})", text, re.MULTILINE | re.DOTALL)
    if not match:
        return {}

    block = match.group(1)
    try:
        return {
            str(key).upper(): tuple(int(v) for v in value)
            for key, value in ast.literal_eval(block).items()
        }
    except Exception as exc:
        print(f"WARNING could not parse {name} from fw/main.py: {exc}", file=sys.stderr)
        return {}


def load_firmware_color_map(path):
    path = Path(path)
    if not path.exists():
        print(f"WARNING firmware file not found, using embedded colors: {path}", file=sys.stderr)
        return FIRMWARE_COLOR_MAP

    text = path.read_text(encoding="utf-8", errors="replace")
    loaded = {}
    loaded.update(parse_color_dict_from_main(text, "PRIMARY"))
    loaded.update(parse_color_dict_from_main(text, "SHADES"))
    if not loaded:
        print(f"WARNING no PRIMARY/SHADES colors parsed from {path}, using embedded colors.", file=sys.stderr)
        return FIRMWARE_COLOR_MAP
    return loaded


def read_lines(stream, prefix, output_queue):
    for line in iter(stream.readline, ""):
        text = line.rstrip()
        if text:
            print(f"{prefix} {text}")
            output_queue.put(text)


def wait_ready(output_queue, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            line = output_queue.get(timeout=0.1)
        except queue.Empty:
            continue
        if "READY sender=" in line:
            return True
    return False


def send(process, command):
    print(f"TX {command}")
    process.stdin.write(command + "\n")
    process.stdin.flush()


def pct_command(mode, include_review_controls, r, g, b):
    command = "ALLPCTPANEL" if mode in ("review", "palette") and include_review_controls else "ALLPCT"
    return f"{command} {r} {g} {b}"


def calibration_scope(mode, include_review_controls):
    if mode in ("review", "palette") and include_review_controls:
        return "full-panel-with-start-select-load"
    return "buttons-8"


def expected_color_name(r_pct, g_pct, b_pct):
    pct = (r_pct, g_pct, b_pct)
    exact = [
        name for name in PREFERRED_COLOR_ORDER
        if FIRMWARE_COLOR_MAP.get(name) == pct
    ]
    if exact:
        return "/".join(exact)

    nearest = min(
        PREFERRED_COLOR_ORDER,
        key=lambda name: sum(
            (pct[channel] - FIRMWARE_COLOR_MAP[name][channel]) ** 2
            for channel in range(3)
        ),
    )
    return f"near {nearest}"


def parse_values(text):
    values = []
    for part in str(text or "").replace(";", ",").split(","):
        part = part.strip()
        if not part:
            continue
        value = int(part)
        if value < 0 or value > 100:
            raise argparse.ArgumentTypeError("values must be between 0 and 100")
        values.append(value)

    if not values:
        raise argparse.ArgumentTypeError("at least one value is required")

    return sorted(set(values))


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


def pct_key(r, g, b):
    return f"{r},{g},{b}"


def parse_filter_terms(text):
    terms = []
    for part in str(text or "").replace(";", ",").split(","):
        part = part.strip().lower()
        if part:
            terms.append(part)
    return terms


def latest_uniform_items(path):
    latest = {}
    for item in load_jsonl(path):
        if item.get("phase") != "uniform":
            continue
        if item.get("uniform") in (True, False):
            latest[item.get("pct")] = item
    return [item for _, item in sorted(latest.items()) if item.get("uniform") is True]


def latest_name_map(path):
    latest = {}
    for item in load_jsonl(path):
        if item.get("phase") != "name":
            continue
        pct = item.get("pct")
        if not pct:
            continue
        if item.get("valid") is False or not str(item.get("name") or "").strip():
            latest.pop(pct, None)
            continue
        latest[pct] = item
    return latest


def firmware_reference_map():
    refs = {}
    for name in PREFERRED_COLOR_ORDER:
        pct_tuple = FIRMWARE_COLOR_MAP.get(name)
        if not pct_tuple:
            continue
        pct = pct_key(*pct_tuple)
        refs.setdefault(pct, []).append(name)

    return refs


def normalized_name(value):
    name = " ".join(str(value or "").strip().split())
    if name.lower() == "turqoise":
        return "Turquoise"
    return name


def lot_for_name(name):
    text = normalized_name(name).lower()
    if not text:
        return None
    if any(word in text for word in ("white", "gray", "black")):
        return "neutral"
    if any(word in text for word in ("yellow", "lemon")):
        return "yellow"
    if any(word in text for word in ("orange", "gold")):
        return "warm"
    if any(word in text for word in ("green", "lime")):
        return "green"
    if any(word in text for word in ("cyan", "aqua", "turquoise", "teal")):
        return "aqua"
    if "blue" in text:
        return "blue"
    if any(word in text for word in ("pink", "red")):
        return "pink"
    if any(word in text for word in ("magenta", "purple", "violet")):
        return "purple"
    return "other"


def print_lots(current_names):
    lots = {name: [] for name in list(LOT_FILTERS.keys()) + ["other"]}
    for pct, item in current_names.items():
        name = normalized_name(item.get("name"))
        lot = lot_for_name(name) or "other"
        lots.setdefault(lot, []).append((name, pct, item.get("expected") or ""))

    for lot in list(LOT_FILTERS.keys()) + ["other"]:
        entries = sorted(lots.get(lot, []), key=lambda item: (item[0].lower(), item[1]))
        if not entries:
            continue
        print(f"[{lot}] {len(entries)}")
        for name, pct, expected in entries:
            print(f"  {name:<14} {pct:<12} {expected}")
        print("")


def consolidated_palette_items(current_names):
    refs_by_pct = firmware_reference_map()
    pcts = set(refs_by_pct.keys()) | set(current_names.keys())
    items = []

    for pct in pcts:
        values = tuple(int(part) for part in pct.split(","))
        refs = refs_by_pct.get(pct, [])
        current_name = normalized_name(current_names.get(pct, {}).get("name"))
        expected = "/".join(refs) if refs else expected_color_name(*values)
        display_name = current_name or (refs[0] if refs else expected)
        items.append({
            "pct": pct,
            "values": values,
            "refs": refs,
            "expected": expected,
            "current_name": current_name,
            "display_name": display_name,
            "lot": lot_for_name(display_name) or lot_for_name(expected) or "other",
        })

    order = {name: index for index, name in enumerate(PREFERRED_COLOR_ORDER)}

    def sort_key(item):
        refs = item.get("refs") or []
        ref_order = min([order.get(ref, 999) for ref in refs] or [999])
        return (item.get("lot") or "other", ref_order, item.get("display_name") or "", item["pct"])

    return sorted(items, key=sort_key)


def answered_uniform_keys(path):
    answered = set()
    for item in load_jsonl(path):
        if item.get("phase") != "uniform":
            continue
        if item.get("uniform") in (True, False):
            pct = item.get("pct")
            if pct:
                answered.add(pct)
    return answered


def ask_uniform():
    while True:
        answer = input("Uniform? y/n, Enter=skip, q=quit > ").strip().lower()
        if answer in ("q", "quit", "exit"):
            return "quit"
        if answer in ("", "skip", "s"):
            return None
        if answer in ("y", "yes", "o", "oui"):
            return True
        if answer in ("n", "no", "non"):
            return False
        print("Please answer y, n, Enter, or q.")


def ask_review(current_name):
    prompt = "Name"
    if current_name:
        prompt += f" [{current_name}]"
    prompt += ", Enter=keep/skip, '-'=invalid, q=quit > "

    answer = input(prompt).strip()
    if answer.lower() in ("q", "quit", "exit"):
        return "quit"
    if answer in ("-", "invalid", "none", "no"):
        return ""
    if not answer:
        return None
    return answer


def item_matches_filter(item, filters):
    if not filters:
        return True

    haystack = " ".join([
        str(item.get("expected") or ""),
        str(item.get("current_name") or ""),
        " ".join(item.get("refs") or []),
        str(item.get("lot") or ""),
        str(item.get("pct") or ""),
    ]).lower()
    return any(term in haystack for term in filters)


def main():
    parser = argparse.ArgumentParser(
        description="Interactive full-panel RGB percentage sweep for Pico GPIO RGB buttons."
    )
    parser.add_argument("--sender", default="PicoCommandSender.exe")
    parser.add_argument("--ini", default="PicoCommandSender.ini")
    parser.add_argument("--sender-id", default="P1")
    parser.add_argument("--ready-timeout", type=float, default=18.0)
    parser.add_argument("--settle-ms", type=int, default=150)
    parser.add_argument("--values", type=parse_values, default=parse_values("0,25,50,75,100"))
    parser.add_argument("--mode", choices=("uniform", "name", "review", "palette", "lots"), default="uniform")
    parser.add_argument("--results", default="state/color_pct_uniformity.jsonl")
    parser.add_argument("--fw-main", default="fw/main.py", help="Firmware main.py used as the color reference.")
    parser.add_argument("--keep-last", action="store_true", help="Do not clear the panel at the end.")
    parser.add_argument("--rerun", action="store_true", help="Replay all combinations, including already answered ones.")
    parser.add_argument(
        "--review-buttons-only",
        action="store_true",
        help="In review mode, light only B1-B8 instead of B1-B8 plus START/SELECT.",
    )
    parser.add_argument(
        "--filter",
        default="",
        help="Comma-separated terms used by review mode, matched on expected color, current name, or pct.",
    )
    parser.add_argument(
        "--lot",
        choices=sorted(LOT_FILTERS.keys()),
        help="Preset review lot: neutral, yellow, warm, green, aqua, blue, pink, or purple.",
    )
    args = parser.parse_args()

    global FIRMWARE_COLOR_MAP
    FIRMWARE_COLOR_MAP = load_firmware_color_map(args.fw_main)

    results_path = Path(args.results)
    values = args.values
    current_names = latest_name_map(results_path)
    if args.mode == "lots":
        print_lots(current_names)
        return 0
    if args.lot:
        args.filter = ",".join(part for part in (args.filter, LOT_FILTERS[args.lot]) if part)
    review_items = []
    if args.mode == "name":
        uniform_items = latest_uniform_items(results_path)
        combos = [
            tuple(int(part) for part in item["pct"].split(","))
            for item in uniform_items
            if item.get("pct")
        ]
    elif args.mode == "review":
        filters = parse_filter_terms(args.filter)
        for item in latest_uniform_items(results_path):
            pct = item.get("pct")
            if not pct:
                continue
            current_name = current_names.get(pct, {}).get("name")
            pct_values = [int(part) for part in pct.split(",")]
            review_item = {
                "pct": pct,
                "command": item.get("command") or f"ALLPCT {pct.replace(',', ' ')}",
                "expected": expected_color_name(*pct_values),
                "current_name": current_name,
            }
            if item_matches_filter(review_item, filters):
                review_items.append(review_item)
        review_items.sort(key=lambda item: (str(item.get("current_name") or ""), str(item.get("expected") or ""), item["pct"]))
        combos = [
            tuple(int(part) for part in item["pct"].split(","))
            for item in review_items
        ]
    elif args.mode == "palette":
        filters = parse_filter_terms(args.filter)
        review_items = [
            item for item in consolidated_palette_items(current_names)
            if item_matches_filter(item, filters)
        ]
        combos = [item["values"] for item in review_items]
    else:
        combos = list(itertools.product(values, repeat=3))
        if not args.rerun:
            all_count = len(combos)
            answered = answered_uniform_keys(results_path)
            combos = [
                combo for combo in combos
                if pct_key(*combo) not in answered
            ]
            skipped = all_count - len(combos)
            if skipped:
                print(f"Resume: skipped {skipped} already answered combinations from {results_path}.")
                print("Use --rerun to replay every combination.")
    if not combos:
        print("No remaining combinations.")
        return 0
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
        if not wait_ready(output_queue, args.ready_timeout):
            print("ERROR sender did not become READY in time.", file=sys.stderr)
            return 2

        print("")
        if args.mode == "name":
            print(f"Replaying uniform combinations from {results_path}.")
        elif args.mode == "review":
            print(f"Reviewing uniform combinations from {results_path}.")
            if args.filter:
                print(f"Filter: {args.filter}")
        elif args.mode == "palette":
            print("Replaying consolidated firmware + validated palette.")
            if args.filter:
                print(f"Filter: {args.filter}")
        else:
            print(f"Full-panel sweep B1-B8 with percentages {','.join(str(v) for v in values)}.")
        print(f"Total combinations: {len(combos)}")
        if args.mode in ("review", "palette") and not args.review_buttons_only:
            print("Firmware command used: ALLPCTPANEL r g b")
            print("Review lights B1-B8 plus START/SELECT as ON/OFF load when present.")
        else:
            print("Firmware command used: ALLPCT r g b")
        print("Note: r/g/b are extinction percentages, so 0=on and 100=off.")
        if args.mode == "uniform":
            print(f"Answers are saved to {results_path}.")
            print("Use y/n to mark uniformity, Enter to skip, q to quit.")
        elif args.mode in ("review", "palette"):
            print(f"Reviewed names are appended to {results_path}.")
            if args.mode == "palette":
                print("Enter validates the displayed name, type a new name to rename, '-' marks invalid, q quits.")
            else:
                print("Enter keeps/skips the current value, type a new name to rename, '-' marks invalid, q quits.")
        else:
            print(f"Color names are appended to {results_path}.")
            print("Type the perceived color name, Enter to skip, q to quit.")
        print("")

        for index, (r, g, b) in enumerate(combos, start=1):
            pct = pct_key(r, g, b)
            label = f"PCT:{pct}"
            expected = expected_color_name(r, g, b)
            refs = []
            if args.mode == "palette":
                palette_item = review_items[index - 1]
                refs = palette_item.get("refs") or []
                expected = palette_item.get("expected") or expected
            command = pct_command(args.mode, not args.review_buttons_only, r, g, b)
            scope = calibration_scope(args.mode, not args.review_buttons_only)
            current_name = None
            if args.mode in ("review", "palette"):
                current_name = current_names.get(pct, {}).get("name")
            if args.mode == "palette" and not current_name and refs:
                current_name = refs[0]
            if refs:
                print(f"[{index:03d}/{len(combos)}] {label} refs={'/'.join(refs)} expected={expected} current={current_name or '-'}")
            elif current_name:
                print(f"[{index:03d}/{len(combos)}] {label} expected={expected} current={current_name}")
            else:
                print(f"[{index:03d}/{len(combos)}] {label} expected={expected}")
            send(process, command)
            time.sleep(max(0, args.settle_ms) / 1000)

            if args.mode == "uniform":
                uniform = ask_uniform()
                if uniform == "quit":
                    break
                append_jsonl(results_path, {
                    "phase": "uniform",
                    "pct": pct,
                    "command": command,
                    "expected": expected,
                    "uniform": uniform,
                    "scope": scope,
                    "button_count": 8,
                    "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                })
            elif args.mode in ("review", "palette"):
                answer = ask_review(current_name)
                if answer == "quit":
                    break
                if answer == "":
                    append_jsonl(results_path, {
                        "phase": "name",
                        "pct": pct,
                        "command": command,
                        "expected": expected,
                        "name": "",
                        "valid": False,
                        "scope": scope,
                        "button_count": 8,
                        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                    })
                    current_names.pop(pct, None)
                elif answer is None and args.mode == "palette" and current_name:
                    append_jsonl(results_path, {
                        "phase": "name",
                        "pct": pct,
                        "command": command,
                        "expected": expected,
                        "name": current_name,
                        "valid": True,
                        "scope": scope,
                        "button_count": 8,
                        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                    })
                    current_names[pct] = {"name": current_name}
                elif answer is not None:
                    append_jsonl(results_path, {
                        "phase": "name",
                        "pct": pct,
                        "command": command,
                        "expected": expected,
                        "name": answer,
                        "valid": True,
                        "scope": scope,
                        "button_count": 8,
                        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                    })
                    current_names[pct] = {"name": answer}
            else:
                answer = input("Color name, Enter=skip, q=quit > ").strip()
                if answer.lower() in ("q", "quit", "exit"):
                    break
                if answer:
                    append_jsonl(results_path, {
                        "phase": "name",
                        "pct": pct,
                        "command": command,
                        "expected": expected,
                        "name": answer,
                        "valid": True,
                        "scope": scope,
                        "button_count": 8,
                        "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
                    })

        if not args.keep_last:
            send(process, "ALL BLACK")
            time.sleep(0.2)

        return 0
    finally:
        try:
            process.stdin.close()
        except Exception:
            pass
        try:
            process.terminate()
            process.wait(timeout=2)
        except Exception:
            try:
                process.kill()
            except Exception:
                pass


if __name__ == "__main__":
    raise SystemExit(main())
