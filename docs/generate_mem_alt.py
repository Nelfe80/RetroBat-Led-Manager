import json
import os
import re
import shutil
import sys
import api_lexicon

from parser_dict import FAMILY_ROUTING, ACTION_KEYWORDS

LB_GENRES_CACHE = None

GENRE_SYSTEM_MAP = {
    "snes": "super nintendo entertainment system",
    "megadrive": "sega genesis",
    "nes": "nintendo entertainment system",
    "gb": "nintendo game boy",
    "gba": "nintendo game boy advance",
    "gbc": "nintendo game boy color",
    "msx": "microsoft msx",
    "msx2": "microsoft msx2",
    "mastersystem": "sega master system",
    "gamegear": "sega game gear",
    "n64": "nintendo 64",
    "psx": "sony playstation",
    "pce": "nec turbografx-16",
    "pcengine": "nec turbografx-16",
    "tg16": "nec turbografx-16",
    "mame": "arcade",
    "arcade": "arcade",
    "neogeo": "snk neo geo mvs",
    "32x": "sega 32x",
    "segacd": "sega cd"
}

def get_genre(system, title):
    """
    Détermine le genre via la base LaunchBox (lb_genres.json).
    Fallback: 'Platform'
    """
    global LB_GENRES_CACHE
    db_path = r"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\databases\lb_genres.json"
    
    if LB_GENRES_CACHE is None:
        if os.path.exists(db_path):
            try:
                with open(db_path, "r", encoding="utf-8") as f:
                    LB_GENRES_CACHE = json.load(f)
            except:
                LB_GENRES_CACHE = {}
        else:
            LB_GENRES_CACHE = {}

    lb_system = GENRE_SYSTEM_MAP.get(system.lower(), system.lower())
    if lb_system not in LB_GENRES_CACHE:
        # Tentative de recherche floue sur les clés de système si pas de mapping direct
        for k in LB_GENRES_CACHE.keys():
            if system.lower() in k.lower():
                lb_system = k
                break

    if lb_system in LB_GENRES_CACHE:
        sys_db = LB_GENRES_CACHE[lb_system]
        t_norm = title.lower().strip()
        # 1. Correspondance exacte
        if t_norm in sys_db:
            return sys_db[t_norm]
        # 2. Correspondance "slug" (sans ponctuation)
        t_slug = re.sub(r'[^a-z0-9]', '', t_norm)
        for g_title, g_genre in sys_db.items():
            if re.sub(r'[^a-z0-9]', '', g_title.lower()) == t_slug:
                return g_genre
        # 3. Correspondance "commence par" (Fuzzy light)
        for g_title, g_genre in sys_db.items():
            if t_norm.startswith(g_title.lower()) or g_title.lower().startswith(t_norm):
                return g_genre

    return "Platform"

# Mapping des nouvelles catégories DataCrystal vers le standard V11
DC_CATEGORY_MAP = {
    "level": "progression.zone",
    "coins_rings": "scoring.collectibles",
    "powerup_state": "state.temporary",
    "star_invincibility": "state.temporary",
    "enemy_state": "combat.enemies",
    "mode_state": "flow.lifecycle",
    "x_position": "system.movement",
    "y_position": "system.movement",
    "memory": "system.memory",
    "events": "flow.lifecycle",
    "stage": "progression.stage",
    "zone": "progression.zone",
    "game_state": "flow.lifecycle",
    "settings": "flow.settings",
    "oxygen": "resources.lives",
    "lives": "resources.lives",
    "scoring": "scoring.points"
}

# Suppression de DC_ACTION_DEFAULTS par précaution car l'utilisateur veut tester SANS fallback auto.

def clean_desc(desc):
    """Nettoie les descriptions RA (enlève adresses hex, etc)."""
    import re
    return re.sub(r'\(0x[0-9A-F]+\)', '', desc).strip()

def find_json_source(system, game_id):
    """Trouve le fichier JSON dans gamehacking matching le slug."""
    # Priorité au dossier industriel
    base_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\gamehacking\{system}"
    if not os.path.exists(base_dir): 
        # Fallback ra si pas encore standardisé
        base_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\ra\{system}"
    
    if not os.path.exists(base_dir): return None
    for f in os.listdir(base_dir):
        if f.startswith(game_id) and f.endswith(".json"):
            return os.path.join(base_dir, f)
    return None

def find_doflinx_source(system, game_id):
    """Trouve le fichier MEM dans sources/doflinx matching le game_id."""
    base_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\doflinx\{system}"
    if not os.path.exists(base_dir):
        # Fallback racine doflinx (ex: arcade sans sous-dossier système)
        base_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\doflinx"
    if not os.path.exists(base_dir): return None
    exact_path = os.path.join(base_dir, f"{game_id}.MEM")
    if os.path.exists(exact_path): return exact_path
    for f in os.listdir(base_dir):
        if f.lower().startswith(game_id.lower()) and f.endswith(".MEM"):
            return os.path.join(base_dir, f)
    return None

def find_dc_source(system, game_id):
    """Trouve le fichier MEM dans sources/datacrystal/{system} matching le game_id."""
    base_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\datacrystal\{system}"
    if not os.path.exists(base_dir): return None
    exact_path = os.path.join(base_dir, f"{game_id}.MEM")
    if os.path.exists(exact_path): return exact_path
    for f in os.listdir(base_dir):
        if f.lower().startswith(game_id.lower()) and f.endswith(".MEM"):
            return os.path.join(base_dir, f)
    return None

def get_ra_metadata(system, game_id):
    """Récupère le titre propre et les hashes depuis sources/ra JSON."""
    ra_path = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\ra\{system}\{game_id}.json"
    if not os.path.exists(ra_path):
        # Fallback : cherche n'importe quel fichier commençant par game_id
        base_dir = os.path.dirname(ra_path)
        if os.path.exists(base_dir):
            for f in os.listdir(base_dir):
                if f.startswith(game_id) and f.endswith(".json"):
                    ra_path = os.path.join(base_dir, f)
                    break
    
    if os.path.exists(ra_path):
        try:
            with open(ra_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
                return data.get("title", game_id), data.get("hashes", [])
        except: pass
    return game_id, []

def apply_logging_rules(fam, entry, lifecycle_tracker=None):
    """
    Applique la règle globale de no_log/no_survey (Standard Industriel V11.18).
    Default: True pour tout (Silencieux).
    Whitelist (False) -> Visible :
    - Survie: HIT, HEAL, LIVES_STATE, GAIN_LIFE, LOSE_LIFE
    - Combat: BOSS_HIT, BOSS_DEFEATED, WEAPON_UPGRADE
    - Ressources: COIN_GAIN, COIN_LOSE, MONEY_STATE
    - Etats: TRANSFORMATION_*, MOUNT_*, INVINCIBILITY_*, SHIELD_*
    - Butins: TREASURE, KEY_GET
    - Lifecycle (Unique): TITLE_SCREEN, LEVEL_CLEAR, GAME_OVER, NEW_LEVEL, SELECT_SCREEN
    """
    desc = entry.get("desc", "").lower()
    action = entry.get("action", "")
    
    # 1. Définition de la Whitelist
    whitelist_actions = [
        "HIT", "HEAL", "LIVES_STATE", "GAIN_LIFE", "LOSE_LIFE",
        "BOSS_HIT", "BOSS_DEFEATED", "WEAPON_UPGRADE",
        "COIN_GAIN", "COIN_LOSE", "MONEY_STATE", "SCORE_STATE", "EXPERIENCE_STATE",
        "TREASURE", "KEY_GET", "INVINCIBILITY_START", "INVINCIBILITY_STOP", "SHIELD_GAIN", "SHIELD_LOST",
        "PROGRESSION_ZONE", "PROGRESSION_STAGE"
    ]
    
    is_whitelisted = (action in whitelist_actions) or action.startswith("TRANSFORMATION") or action.startswith("MOUNT") or action.startswith("PLAYER_STATE")
    
    # 2. Gestion Spéciale Lifecycle (Unicité)
    lifecycle_actions = [
        "TITLE_SCREEN", "LEVEL_CLEAR", "GAME_OVER", "NEW_LEVEL", "SELECT_SCREEN", 
        "GAME_PLAYING", "PAUSE_ON", "PAUSE_OFF", "DEMO_MODE", "CONTINUE_SCREEN", "CREDITS_SCREEN",
        "CORPORATE_SCREEN", "CORPORATE_SCREEN_RED", "START_GAME", "INTRO_SCREEN", "LOADING_SCREEN",
        "CHARACTER_SELECT", "STAGE_SELECT", "WORLD_MAP"
    ]
    is_lifecycle = (action in lifecycle_actions)
    
    # 3. Detection Anti-Spam (V11.25)
    # Si c'est un timer/compteur, on est invisible dans le LOG mais actif pour SURVEY/UDP
    spam_keywords = ["timer", "counter", "duration", "flash", "clignotement", "protection", "frame", "invuln", "invincibility", "invulnerability"]
    full_text = f"{desc} {entry.get('comment', '').lower()}"
    is_spam = any(k in full_text for k in spam_keywords) or any(k in action.lower() for k in spam_keywords)
    
    # 4. Détection Son/Musique (V11.27) -> On privilégie les valeurs réelles, on tait les sons
    sound_keywords = ["sound effect", "sfx", "music", "musique", "sonore", "effet sonore"]
    is_sound = any(k in full_text for k in sound_keywords)
    
    should_be_visible = False
    if is_whitelisted:
        should_be_visible = True
    elif is_lifecycle and lifecycle_tracker is not None and not is_sound:
        if action not in lifecycle_tracker:
            lifecycle_tracker.add(action)
            should_be_visible = True
            
    if should_be_visible:
        entry["no_log"] = False
        entry["no_survey"] = False
    else:
        entry["no_log"] = True
        entry["no_survey"] = True
        
    # Overwrite pour les sons (Sourdine log mais actif SURVEY)
    if is_sound:
        entry["no_log"] = True
        entry["no_survey"] = False
        
    # Overwrite pour les timers/compteurs (On veut le survey mais PAS le log)
    if is_spam:
        entry["no_log"] = True
        entry["no_survey"] = False

def get_matching_keywords(desc, action_hint=None):
    """
    Trouve les mots-clés qui matchent dans la description.
    Si action_hint est fourni, on regarde en priorité le regex de cette action.
    """
    if not desc: return ""
    desc_lower = desc.lower()
    matches = set()
    
    # 1. On regarde d'abord les mots-clés de l'action associée (Priorité)
    if action_hint and action_hint in ACTION_KEYWORDS:
        regex = ACTION_KEYWORDS[action_hint]
        patterns = re.findall(r'\(?([a-z0-9|\ \-]+)\)?', regex)
        for p in patterns:
            for word in p.split('|'):
                word = word.strip()
                if not word or word in ["inactive", "off", "no", "not active", "lost"]: continue
                if re.search(r'\b' + re.escape(word) + r'\b', desc_lower):
                    matches.add(word)

    # 2. Si rien trouvé via l'action, on cherche dans TOUT le dictionnaire
    if not matches:
        for act, regex in ACTION_KEYWORDS.items():
            patterns = re.findall(r'\(?([a-z0-9|\ \-]+)\)?', regex)
            for p in patterns:
                for word in p.split('|'):
                    word = word.strip()
                    if not word or word in ["inactive", "off", "no", "not active", "lost", "game", "state"]: continue
                    if re.search(r'\b' + re.escape(word) + r'\b', desc_lower):
                        matches.add(word)
                        
    return ", ".join(sorted(list(matches)))

def parse_lua_block_to_dict(lua_text):
    """Analyseur minimaliste de tables Lua vers dict Python."""
    res = {}
    pairs = re.finditer(r'\[([^\]]+)\]\s*=\s*([^,}]+)', lua_text)
    for p in pairs:
        k_str = p.group(1).strip()
        v_str = p.group(2).strip()
        if k_str.startswith('0x') or k_str.startswith('0X'): key = int(k_str, 16)
        else:
            try: key = int(k_str)
            except: key = k_str.replace('"', '').replace("'", "")
        if v_str.startswith('{'):
            sub_res = {}
            for sub_p in re.finditer(r'([a-z_]+)\s*=\s*([^,}]+)', v_str):
                sk = sub_p.group(1); sv = sub_p.group(2).strip().replace('"', '').replace("'", "")
                if sv == "true": sv = True
                elif sv == "false": sv = False
                sub_res[sk] = sv
            res[key] = sub_res
        else:
            res[key] = v_str.replace('"', '').replace("'", "")
    return res

def parse_entries_from_text(block_content, default_family, system_prefix, cat_name=None, is_dc=True, lifecycle_tracker=None, source_label="doflinx"):
    """Extrait toutes les entrées { address=... } d'un bloc de texte."""
    extracted = []
    pos = 0
    while True:
        start = block_content.find("{ address=", pos)
        if start == -1: start = block_content.find("{address=", pos)
        if start == -1: break
        bc = 0; end = -1
        for i in range(start, len(block_content)):
            if block_content[i] == '{': bc += 1
            elif block_content[i] == '}': 
                bc -= 1
                if bc == 0: end = i; break
        if end == -1: break
        entry_text = block_content[start:end+1]; pos = end + 1
        addr_m = re.search(r'address=(0[xX][0-9a-fA-F]+)', entry_text)
        if not addr_m: continue
        addr = int(addr_m.group(1), 16)
        if addr < 0x100000 and system_prefix > 0: addr |= system_prefix
        tipo = re.search(r'type="([^"]+)"', entry_text)
        cond = re.search(r'condition="([^"]+)"', entry_text)
        desc_m = re.search(r'desc="([^"]+)"', entry_text)
        act_m = re.search(r'action="([^"]+)"', entry_text)
        val_m = re.search(r'value=([^,\s}]+)', entry_text)
        min_v = re.search(r'min=([^,\s}]+)', entry_text); max_v = re.search(r'max=([^,\s}]+)', entry_text)
        nolog = re.search(r'no_log=(true|false)', entry_text); nosurv = re.search(r'no_survey=(true|false)', entry_text)
        orig_desc = desc_m.group(1) if desc_m else ""
        entry = {"address": addr, "type": tipo.group(1) if tipo else "u8", "condition": cond.group(1) if cond else "change", "desc": orig_desc}
        if act_m: entry["action"] = act_m.group(1)
        if val_m:
            v_s = val_m.group(1).replace('"', '')
            if v_s.startswith('0x') or v_s.startswith('0X'): entry["value"] = int(v_s, 16)
            else:
                try: entry["value"] = int(v_s)
                except: entry["value"] = v_s
        if min_v: entry["min"] = min_v.group(1)
        if max_v: entry["max"] = max_v.group(1)
        if nolog: entry["no_log"] = (nolog.group(1) == "true")
        if nosurv: entry["no_survey"] = (nosurv.group(1) == "true")
        entry["source"] = source_label if is_dc else "ra"
        
        effective_act = entry.get("action")
        final_family = default_family
        
        # Transformation de la description pour Data Crystal
        if is_dc:
            entry["comment"] = orig_desc
            if not effective_act:
                effective_act = api_lexicon.get_action_for_label(orig_desc)
            
            # Application de la règle "Keyword Only"
            kws = get_matching_keywords(orig_desc, effective_act)
            entry["desc"] = kws
            
            # Si aucun mot-clé trouvé, on ne définit pas d'action et on bascule en system.memory
            if not kws:
                if "action" in entry: del entry["action"]
                effective_act = None
                final_family = "system.memory"
            else:
                if effective_act: entry["action"] = effective_act
                final_family = FAMILY_ROUTING.get(effective_act, default_family)
        
        # Normalisation automatique des conditions (Anti-Spam V11.24)
        if entry.get("condition") == "change":
            analysis_label = (entry.get("desc", "") or "") + " " + (entry.get("comment", "") or "")
            entry["condition"] = api_lexicon.get_best_condition(entry.get("action", ""), label=analysis_label)

        for tfield in ["map", "action_map"]:
            f_start = entry_text.find(f"{tfield}=")
            if f_start != -1:
                o_br = entry_text.find("{", f_start); bc2 = 0; c_br = -1
                for j in range(o_br, len(entry_text)):
                    if entry_text[j] == '{': bc2 += 1
                    elif entry_text[j] == '}': bc2 -= 1; (c_br := j) if bc2 == 0 else None
                    if bc2 == 0: break
                if c_br != -1: entry[tfield] = parse_lua_block_to_dict(entry_text[o_br:c_br+1])

        if "map" in entry and "action_map" not in entry:
            new_am = {}
            for k, label in entry["map"].items():
                found_act = api_lexicon.get_action_for_label(label)
                if not found_act and cat_name: found_act = api_lexicon.get_action_for_label(f"{cat_name} {label}")
                if found_act: new_am[k] = found_act
            if new_am: entry["action_map"] = new_am

        if "action_map" in entry:
            am = entry["action_map"]; m_labels = entry.get("map", {}); base_desc = entry.get("desc"); base_comm = entry.get("comment", orig_desc)
            for v_val, v_act_data in am.items():
                e_copy = entry.copy()
                if "action_map" in e_copy: del e_copy["action_map"]
                if "map" in e_copy: del e_copy["map"]
                e_copy["condition"] = "eq"; e_copy["value"] = v_val
                label = m_labels.get(v_val, str(v_val))
                if is_dc:
                    e_copy["desc"] = get_matching_keywords(label, e_copy.get("action") if isinstance(v_act_data, str) else v_act_data.get("action") if isinstance(v_act_data, dict) else None)
                    e_copy["comment"] = f"{base_comm} ({label})"
                    if not e_copy["desc"]: 
                        if "action" in e_copy: del e_copy["action"]
                        eff_act = None
                        fam = "system.memory"
                    else:
                        if isinstance(v_act_data, str): e_copy["action"] = v_act_data
                        elif isinstance(v_act_data, dict):
                            for k, v in v_act_data.items(): e_copy[k] = v
                        eff_act = e_copy.get("action")
                        fam = FAMILY_ROUTING.get(eff_act, default_family)
                else:
                    e_copy["desc"] = f"{orig_desc} ({label})"
                    if isinstance(v_act_data, str): e_copy["action"] = v_act_data
                    elif isinstance(v_act_data, dict):
                        for k, v in v_act_data.items(): e_copy[k] = v
                    eff_act = e_copy.get("action")
                    fam = FAMILY_ROUTING.get(eff_act, default_family)
                apply_logging_rules(fam, e_copy, lifecycle_tracker); extracted.append((fam, e_copy))
        else:
            apply_logging_rules(final_family, entry, lifecycle_tracker); extracted.append((final_family, entry))
    return extracted

def extract_from_mem(file_path, sys_prefix, lifecycle_tracker=None, source_label="doflinx"):
    """Extrait d'un fichier .MEM (LUA) avec support du nouveau format nested DC."""
    if not os.path.exists(file_path): return []
    with open(file_path, "r", encoding="utf-8") as f: content = f.read()
    ev_range = re.search(r'events\s*=\s*\{(.*)', content, re.DOTALL)
    if not ev_range: return []
    events_content = ev_range.group(1)
    results = []
    cats = re.finditer(r'\["([^"]+)"\]\s*=\s*\{', events_content)
    cat_positions = []
    for c in cats:
        cat_name = c.group(1); start_idx = c.end() - 1; bc = 0; end_idx = -1
        for i in range(start_idx, len(events_content)):
            if events_content[i] == '{': bc += 1
            elif events_content[i] == '}':
                bc -= 1
                if bc == 0: end_idx = i; break
        if end_idx != -1: cat_positions.append((cat_name, start_idx, end_idx))
    if cat_positions:
        for cat_name, s, e in cat_positions:
            block = events_content[s:e+1]; family = DC_CATEGORY_MAP.get(cat_name, "game_state")
            results.extend(parse_entries_from_text(block, family, sys_prefix, cat_name, is_dc=True, lifecycle_tracker=lifecycle_tracker, source_label=source_label))
    else:
        results.extend(parse_entries_from_text(events_content, "game_state", sys_prefix, is_dc=True, lifecycle_tracker=lifecycle_tracker, source_label=source_label))
    return results

def process_node_alt(game_id, node, system_prefix, sys_name, system="", lifecycle_tracker=None):
    """Analyse un node JSON RA."""
    addr_raw = int(node["address"].replace("0x", ""), 16)
    # Support des prefixes Libretro/Flycast
    full_addr = system_prefix | addr_raw if (system_prefix > 0 and addr_raw < system_prefix) else addr_raw
    
    # Correction : L'Industrial JSON utilise 'description' au lieu de 'name'
    name = node.get("description") or node.get("name", "")
    values = node.get("values", [])
    flags = node.get("flags", [])
    
    v_map, v_action_map, metadata, v_bit_map = api_lexicon.process_mapping(game_id, full_addr, values, flags)
    
    global_action = None
    if not v_action_map and not v_bit_map: 
        global_action = api_lexicon.get_action_for_label(name)
    
    if not v_action_map and not global_action and not v_bit_map: 
        return []
    results = []
    v_type = metadata.get("force_type", "u8" if node.get("type") == "8-bit" or system == "nes" else "u16be" if node.get("type") == "16-bit" else "u32be")
    base_desc = metadata.get("force_desc", clean_desc(name))
    if v_bit_map:
        for b_idx, act in v_bit_map.items():
            mask = (1 << b_idx); fam = FAMILY_ROUTING.get(act, "game_state"); label = next((f.get("label", "Active") for f in flags if f.get("bit_index") == b_idx), "Active")
            for cond, suffix in [("bit_true", ""), ("bit_false", " stopped")]:
                e = {"address": full_addr, "type": v_type, "condition": cond, "bit": b_idx, "mask": mask, "desc": f"{base_desc} ({label}{suffix})", "action": act if cond=="bit_true" else act.replace("_START", "_STOP"), "source": "ra"}
                apply_logging_rules(fam, e, lifecycle_tracker); results.append((fam, e))
        return results
    if v_action_map:
        for val, act in v_action_map.items():
            if act == "UPDATE": continue
            fam = FAMILY_ROUTING.get(act, "game_state"); label = v_map.get(val, act)
            e = {"address": full_addr, "type": v_type, "condition": "eq", "value": val, "action": act, "desc": f"{base_desc} ({label})" if label != act else base_desc, "source": "ra"}
            apply_logging_rules(fam, e, lifecycle_tracker); results.append((fam, e))
        return results
    elif global_action:
        fam = FAMILY_ROUTING.get(global_action, "game_state")
        e = {"address": full_addr, "type": v_type, "condition": api_lexicon.get_best_condition(global_action, label=base_desc), "desc": base_desc, "action": global_action, "source": "ra"}
        apply_logging_rules(fam, e, lifecycle_tracker); results.append((fam, e))
    return results

def dict_to_lua(d, indent=2):
    """Convertit un dict Python en Lua Nested."""
    lines = []
    space = "  " * indent
    for k in sorted(d.keys()):
        v = d[k]
        if isinstance(v, dict):
            lines.append(f"{space}{k} = {{")
            lines.append(dict_to_lua(v, indent + 1))
            lines.append(f"{space}}},")
        elif isinstance(v, list):
            lines.append(f"{space}{k} = {{")
            for item in v:
                parts = []
                for key in ["address", "type", "condition", "value", "mask", "bit", "min", "max", "action", "source", "no_log", "no_survey", "desc"]:
                    if key in item:
                        val = item[key]
                        if val is None: continue
                        if key == "address":
                            if isinstance(val, str) and (val.startswith("0x") or val.startswith("0X")):
                                val = int(val, 16)
                            parts.append(f"address=0X{val:06X}" if isinstance(val, int) else f'address="{val}"')
                        elif key == "value":
                            if isinstance(val, str) and (val.startswith("0x") or val.startswith("0X")):
                                val = int(val, 16)
                            parts.append(f"value=0X{val:02X}" if isinstance(val, int) else f'value="{val}"')
                        elif key == "mask":
                            if isinstance(val, str) and (val.startswith("0x") or val.startswith("0X")):
                                val = int(val, 16)
                            parts.append(f"mask=0X{val:02X}" if isinstance(val, int) else f'mask="{val}"')
                        elif isinstance(val, bool): 
                            parts.append(f"{key}={'true' if val else 'false'}")
                        elif isinstance(val, str): 
                            parts.append(f'{key}="{val}"')
                        else: 
                            parts.append(f"{key}={val}")
                lines.append(f"{space}  {{ {', '.join(parts)} }},")
            lines.append(f"{space}}},")
    return "\n".join(lines)

def generate_mem_alt(system, game_id, custom_key=None):
    # Setup de base
    sys_name = "Genesis/Mega Drive" if system == "megadrive" else system.capitalize()
    sys_prefix = {"megadrive": 0xFF0000, "snes": 0x7E0000, "nes": 0x0000}.get(system, 0x0)
    
    events_tree = {}; master_dict = {}
    lifecycle_tracker = set()
    action_authority = {} # { action: source } pour priorisation (V11.23)
    
    # Chargement noise_map.json (V11.24 - Noise Isolation)
    game_slug = re.sub(r'-\d+$', '', game_id).lower()
    noise_map_path = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\mem_gen\{system}\noise_map.json"
    noise_ignored_addrs = set()  # adresses hex à ignorer pour ce jeu
    if os.path.exists(noise_map_path):
        try:
            with open(noise_map_path, 'r', encoding='utf-8') as f:
                noise_map = json.load(f)
            game_noise = noise_map.get(game_slug, {})
            noise_ignored_addrs = {
                addr.upper() for addr, info in game_noise.items()
                if isinstance(info, dict) and info.get('ignore', False)
            }
            if noise_ignored_addrs:
                print(f"[NOISE_MAP] {len(noise_ignored_addrs)} adresses ignorées pour '{game_slug}': {noise_ignored_addrs}")
        except: pass
    
    def add_unique(fam, entry, overwrite=True):
        if not fam or not entry: return
        action = entry.get("action")
        source = entry.get("source")
        
        # NOISE ISOLATION (V11.24) : ignorer les adresses marquées dans noise_map
        raw_addr = entry.get("address", "")
        norm_addr = ("0X" + format(raw_addr, '06X')) if isinstance(raw_addr, int) else str(raw_addr).upper()
        if norm_addr in noise_ignored_addrs:
            return  # Adresse gelée, on skip
        
        # 1. Gestion de la Priorité des Actions (Action Authority)
        # On priorise les évènements de cycle de vie (flow.lifecycle)
        if fam.startswith("flow.lifecycle") and action:
            if action in action_authority:
                # Si déjà présent via une source chargée AVANT (RA > DC > GH), on met en silence
                if action_authority[action] != source:
                    entry["no_log"] = True
                    entry["no_survey"] = True
            else:
                # Premier arrivé (le plus prioritaire), on enregistre l'autorité
                action_authority[action] = source

        # Dédoublonnage d'adresse : On utilise (adresse, condition, valeur, bit)
        key = (entry.get("address"), entry.get("condition"), entry.get("value"), entry.get("bit"))
        
        # Nettoyage descriptions
        if "desc" in entry and entry["desc"]:
            entry["desc"] = entry["desc"].replace("\r\n", " | ").replace("\n", " | ").replace("\r", " | ").strip()[:100]
            
        if "comment" in entry and entry["comment"]:
            entry["comment"] = entry["comment"].replace("\r\n", " | ").replace("\n", " | ").replace("\r", " | ").strip()[:100]
            if entry.get("comment") == entry.get("desc"):
                del entry["comment"]
        
        if key in master_dict:
            # Si déjà présent, on n'écrase que si précisé (RA/DC)
            if overwrite:
                old_entry, old_fam = master_dict[key]
                if len(entry.get("desc", "")) > len(old_entry.get("desc", "")):
                    old_entry.update(entry)
            return
            
        master_dict[key] = (entry, fam)
        parts = fam.split("."); curr = events_tree
        for p in parts[:-1]: curr = curr.setdefault(p, {})
        curr.setdefault(parts[-1], []).append(entry)

    # 1. Traitement RetroAchievements (RA) - Priorité 1 (Base/Key)
    ra_path = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\ra\{system}\{game_id}.json"
    if not os.path.exists(ra_path):
        # On cherche le premier fichier qui commence par game_id
        base_dir = os.path.dirname(ra_path)
        if os.path.exists(base_dir):
            for f in os.listdir(base_dir):
                if f.lower().startswith(game_id.lower()) and f.endswith(".json"):
                    ra_path = os.path.join(base_dir, f)
                    break
    
    # On met à jour l'ID effectif si un fichier RA a été trouvé (ex: sonic-the-hedgehog -> sonic-the-hedgehog-1)
    effective_id = game_id
    if os.path.exists(ra_path):
        effective_id = os.path.basename(ra_path).replace(".json", "")
        print(f"Effective ID mapped: {game_id} -> {effective_id}")
        try:
            with open(ra_path, 'r', encoding='utf-8') as f:
                ra_data = json.load(f)
                print(f"Loading RA source: {ra_path}")
                nodes = ra_data.get("RichPresencePatch", {}).values() or ra_data.get("code_notes", [])
                for node in nodes:
                    for fam, entry in process_node_alt(effective_id, node, sys_prefix, system, system=system, lifecycle_tracker=lifecycle_tracker):
                        add_unique(fam, entry, overwrite=True)
        except Exception as e:
            print(f"Error loading RA data: {e}")

    # 2. Traitement DOFLinx MEM natif (doflinx) - Priorité 2 (Enrichissement arcade)
    doflinx_path = find_doflinx_source(system, effective_id)
    if doflinx_path:
        print(f"Loading DOFLinx source: {doflinx_path}")
        for fam, entry in extract_from_mem(doflinx_path, sys_prefix, lifecycle_tracker=lifecycle_tracker, source_label="doflinx"):
            add_unique(fam, entry, overwrite=False)

    # 3. Traitement DataCrystal (DC) - Priorité 3 (Maps mémoire communautaires)
    dc_path = find_dc_source(system, effective_id)
    if dc_path:
        print(f"Loading DataCrystal source: {dc_path}")
        for fam, entry in extract_from_mem(dc_path, sys_prefix, lifecycle_tracker=lifecycle_tracker, source_label="datacrystal"):
            add_unique(fam, entry, overwrite=False)

    # 4. Traitement GameHacking (GH) - Priorité 4 (Industrialisation pour COMPLÉTION uniquement)
    gh_path = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\sources\gamehacking\{system}\{effective_id}.json"
    if not os.path.exists(gh_path):
        base_dir = os.path.dirname(gh_path)
        if os.path.exists(base_dir):
            for f in os.listdir(base_dir):
                if f.lower().startswith(effective_id.lower()) and f.endswith(".json"):
                    gh_path = os.path.join(base_dir, f)
                    break

    if os.path.exists(gh_path):
        try:
            with open(gh_path, 'r', encoding='utf-8') as f:
                gh_data = json.load(f)
                print(f"Loading GameHacking source: {gh_path}")
                if isinstance(gh_data, list):
                    for node in gh_data:
                        desc = node.get("description") or node.get("name", "")
                        action = api_lexicon.get_action_for_label(desc) or node.get("action", "UNKNOWN")
                        fam = FAMILY_ROUTING.get(action, node.get("family", "unknown"))
                        addr = node["address"]
                        if isinstance(addr, str) and (addr.startswith("0x") or addr.startswith("0X")):
                            addr = int(addr, 16)
                        cond = node.get("condition", "change")
                        label_for_analysis = f"{desc} {node.get('comment', '')}"
                        if cond == "change": cond = api_lexicon.get_best_condition(action, label=label_for_analysis)
                        
                        entry = {
                            "address": addr, "type": node.get("type", "u8"), "condition": cond,
                            "action": action, "desc": desc, "comment": node.get("comment", ""), "source": "gamehacking"
                        }
                        if "values" in node and isinstance(node["values"], dict):
                            for val_key, val_label in node["values"].items():
                                try: v_int = int(val_key)
                                except: 
                                    try: v_int = int(val_key, 16)
                                    except: v_int = val_key
                                e_copy = entry.copy(); e_copy["condition"] = "eq"; e_copy["value"] = v_int; e_copy["source"] = "gamehacking"
                                if val_label and val_label.lower() != "nothing": e_copy["desc"] = f"{entry['desc']} ({val_label})"
                                v_act = api_lexicon.get_action_for_label(val_label)
                                if v_act:
                                    e_copy["action"] = v_act; v_fam = FAMILY_ROUTING.get(v_act, fam)
                                    apply_logging_rules(v_fam, e_copy, lifecycle_tracker); add_unique(v_fam, e_copy, overwrite=False)
                                else:
                                    apply_logging_rules(fam, e_copy, lifecycle_tracker); add_unique(fam, e_copy, overwrite=False)
                        else:
                            apply_logging_rules(fam, entry, lifecycle_tracker); add_unique(fam, entry, overwrite=False)
        except Exception as e:
            print(f"Error loading GameHacking data: {e}")

    # Si rien n'a été trouvé
    if not master_dict:
        print(f"No data found for {system}/{game_id}")
        return

    # POST-PASS : Réveil des adresses siblings (V11.24)
    # Si une adresse est gelée (noise_ignored), on cherche un candidat de même action+famille
    # encore présent mais mis en silence (no_log/no_survey=True), et on le réveille.
    if noise_ignored_addrs:
        # Construire index : action+fam -> [entry, ...]
        action_fam_map = {}
        for key, (entry, fam) in master_dict.items():
            act = entry.get("action")
            if act:
                af_key = f"{act}|{fam}"
                if af_key not in action_fam_map:
                    action_fam_map[af_key] = []
                action_fam_map[af_key].append(entry)

        # Identifier les actions dont la source principale était sur une adresse gelée
        # (on repère celles qui n'ont AUCUN entry actif sans no_log)
        woken = []
        for af_key, entries in action_fam_map.items():
            active = [e for e in entries if not e.get("no_log") and not e.get("no_survey")]
            silenced = [e for e in entries if e.get("no_log") or e.get("no_survey")]
            # Si aucun entry actif mais des candidats silencieux → on réveille le premier
            if not active and silenced:
                candidate = silenced[0]
                candidate.pop("no_log", None)
                candidate.pop("no_survey", None)
                woken.append(f"{af_key} @ {candidate.get('address', '?')}")
        if woken:
            print(f"[WAKE_UP] {len(woken)} adresses réveillées (sibling actif): {woken}")

    # Metadata finales
    ra_title, ra_hashes = get_ra_metadata(system, game_id)
    title = ra_title if ra_title else game_id.replace("-", " ").title()
    
    out_name = re.sub(r'-\d+$', '', game_id).lower()
    out_dir = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\mem_gen\{system}"
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, f"{out_name}.MEM")

    # Construction du bloc rom.hashes
    lua_hashes = ""
    if ra_hashes:
        for h in ra_hashes:
            lua_hashes += f'      {{ hash = "{h.get("hash", "")}", label = "{h.get("label", "")}" }},\n'
    
    # Add the custom_key (physical ROM name) to the internal hashes too (V11.23)
    if custom_key:
        lua_hashes += f'      {{ hash = "{game_id}", label = "{custom_key}" }},\n'
        
    if not lua_hashes:
        id_match = re.search(r'-(\d+)$', game_id)
        g_hash = id_match.group(1) if id_match else game_id
        lua_hashes = f'      {{ hash = "{g_hash}", label = "{game_id}" }},\n'

    mem_content = f"""return {{
  game = {{
    title = "{title}",
    system = "{system}",
    system_name = "{sys_name}",
    genre = "{get_genre(system, title)}"
  }},

  rom = {{
    name = "{title}",
    file = "{out_name}.zip",
    hashes = {{
{lua_hashes.rstrip()}
    }}
  }},

  events = {{
{dict_to_lua(events_tree, 2)}
  }}
}}
"""
    
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(mem_content)
    print(f"Generated (Industrial V11.20): {out_path}")
    
    # Mise à jour des alias centraux et système (V11.23)
    def update_alias(path, key, value):
        data = {}
        if os.path.exists(path):
            try:
                with open(path, 'r', encoding='utf-8') as f: data = json.load(f)
            except: pass
        
        # Add the primary key (custom short name or default game id)
        if key: data[key] = value
        
        # Add the clean slug (value) as a key too for direct matching
        data[value] = value
        
        # Add the raw RA title
        if ra_title: data[ra_title] = value
        
        # Add all specific ROM filenames and hashes from RA source
        for h in ra_hashes:
            label = h.get("label")
            if label:
                data[label] = value
                # Add short name (without extension) if different
                short_name = os.path.splitext(label)[0]
                if short_name != label:
                    data[short_name] = value
            
            if h.get("hash"): data[h["hash"]] = value
        
        try:
            with open(path, 'w', encoding='utf-8') as f: json.dump(data, f, indent=4)
        except: pass

    # 1. Local system-specific alias
    local_sys_alias = rf"c:\Users\vince\Downloads\DOFLinx_V909\API\mem_gen\{system}\alias.json"
    os.makedirs(os.path.dirname(local_sys_alias), exist_ok=True)
    update_alias(local_sys_alias, custom_key, out_name)
    
    # 2. RetroBat system-specific alias
    rb_alias_path = rf"E:\RetroBat\plugins\APIExpose\resources\ram\{system}\alias.json"
    update_alias(rb_alias_path, custom_key, out_name)
    
    print(f"Registry Expanded (V11.23): Local System & RetroBat updated (Central skipped).")

    # Export vers RetroBat
    rb_dir = rf"E:\RetroBat\plugins\APIExpose\resources\ram\{system}"
    try: 
        os.makedirs(rb_dir, exist_ok=True)
        shutil.copy2(out_path, os.path.join(rb_dir, os.path.basename(out_path)))
    except: pass

def main():
    if len(sys.argv) >= 3:
        system = sys.argv[1]
        game_id = sys.argv[2]
        custom_key = sys.argv[3] if len(sys.argv) > 3 else None
        generate_mem_alt(system, game_id, custom_key)

if __name__ == "__main__":
    main()
