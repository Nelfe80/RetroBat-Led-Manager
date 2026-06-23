# api_lexicon.py
import re
import parser_dict

# api_lexicon.py
# Moteur de Traitement Lexical et Sémantique pour l'automatisation DOFLinx API.
# Version 11.16 : Transformation States & Composite Variants

STANDARDIZED_DESCRIPTIONS = {
    "LIVES_STATE": "Lives",
    "HEALTH_STATE": "Health",
    "AMMO_STATE": "Ammo",
    "AMMO_GAIN": "Ammo gained",
    "AMMO_LOSE": "Ammo used",
    "UNIT_GAIN": "Unit gained",
    "UNIT_LOSE": "Unit lost",
    "LOSE_LIFE": "Life lost",
    "GAIN_LIFE": "1UP",
    "HIT": "Take damage",
    "HEAL": "Recover health",
    "DROWNING": "Drowning",
    "LOW_HEALTH_WARN": "Low health warning",
    "RANK_STATE": "Rank",
    "ROUND_STATE": "Round state",
    "STATS_UPDATE": "Stats update",
    "LAP_STATE": "Lap",
    "RESOURCE_STATE": "Resource",
    "RESOURCE_GAIN": "Resource gained",
    "RESOURCE_LOSE": "Resource lost",
    "RESOURCE_USED": "Resource consumed",
    "TIMER_LOW_WARN": "Time low warning",
    "BOSS_HIT": "Boss damage",
    "BOSS_DEFEATED": "Boss defeated",
    "CRITICAL_HIT": "Critical hit",
    "FATALITY": "Fatality",
    "BOMB_FIRED": "Bomb fired",
    "WEAPON_UPGRADE": "Weapon upgrade",
    "COMBO_HIT": "Combo hit",
    "PARRY_SUCCESS": "Parry successful",
    "INVINCIBILITY_START": "Invincibility",
    "INVINCIBILITY_STOP": "Invincibility ends",
    "SHIELD_GAIN": "Shield",
    "SHIELD_LOST": "Shield lost",
    "SPEED_START": "Speed shoes",
    "SPEED_STOP": "Speed shoes ends",
    "SPEED_STATE": "Speed",
    "STEALTH_START": "Stealth active",
    "STEALTH_STOP": "Stealth ends",
    "STATUS_EFFECT_START": "Status effect",
    "STATUS_EFFECT_STOP": "Recover from status",
    "POISON_START": "Poisoned",
    "TRANSFORMATION": "Transformation",
    "STEALTH_ALERT": "Stealth alert",
    "MONEY_STATE": "Rings/Coins",
    "COIN_GAIN": "Collect coin/ring",
    "COIN_LOSE": "Lose coin/ring",
    "KEY_GET": "Key obtained",
    "TREASURE": "Treasure obtained",
    "CHEST_OPENED": "Chest opened",
    "NEW_LEVEL": "Level",
    "TITLE_SCREEN": "Title screen",
    "GAME_OVER": "Game over",
    "CONTINUE_SCREEN": "Continue screen",
    "LEVEL_CLEAR": "Level clear",
    "KEY_PRESSED": "Key pressed",
    "DEMO_MODE": "Demo mode",
    "GAME_PLAYING": "Gameplay",
    "PAUSE_ON": "Paused",
    "PAUSE_OFF": "Unpaused",
    "CORPORATE_SCREEN": "Corporate screen",
    "CREDIT_INSERTED": "Coin inserted",
    "OBJECT_INTERACTION": "World interaction",
    "OBJECT_DESTROYED": "Object destroyed",
    "DOOR_OPENED": "Door opened",
    "GENERAL_TIMER": "Time",
    "BOMB_TIMER": "Bomb timer",
}

DYNAMIC_PATTERNS = [
    (r"\binfinite\s+(.+)", "DYNAMIC_INFINITE"),
    (r"\bmax\s+(.+)", "DYNAMIC_MAX"),
    (r"\bno\s+(.+)", "DYNAMIC_ZERO"),
    (r"\balways\s+(.+)", "DYNAMIC_ALWAYS"),
    (r"\bstart\s+with\s+(.+)", "DYNAMIC_START"),
    (r"\bhave\s+(.+)", "DYNAMIC_INVENTORY"),
    (r"(.+)\s+modifiers?\b", "DYNAMIC_MODIFIER"),
]

# --- ÉVOLUTION V12.2 : MOTEUR DE VARIANTES SÉMANTIQUES COMPOSITES ---
VARIANTS_MAP = {
    "red": "RED", "blue": "BLUE", "green": "GREEN", "yellow": "YELLOW",
    "gold": "GOLD", "silver": "SILVER", "purple": "PURPLE", "black": "BLACK",
}

ITEM_MAP = {
    "mushroom": "MUSHROOM", "flower": "FLOWER", "star": "STAR", "leaf": "LEAF", "feather": "FEATHER",
    "cape": "CAPE", "yoshi": "YOSHI", "egg": "EGG", "balloon": "BALLOON", "cloud": "CLOUD", "shell": "SHELL"
}

STATE_MAP = {
    "big": "BIG", "small": "SMALL", "mini": "MINI", "super": "SUPER", "hyper": "HYPER", "toad": "FROG", "frog": "FROG", "yoshi": "YOSHI"
}

OBJECT_MAP = {
    "coin block": "COINBLOCK", "question block": "QUESTIONBLOCK", "p-block": "PBLOCK", "star block": "STARBLOCK", "note block": "NOTEBLOCK",
    "p-switch": "PSWITCH", "switch": "SWITCH", "pow": "POW", "door": "DOOR",
    "chest": "CHEST", "crate": "CRATE", "barrel": "BARREL", "question mark": "QUESTION",
    "monitor": "MONITOR", "capsule": "CAPSULE", "block": "BLOCK", "blocks": "BLOCK"
}

def get_action_variant(label, base_action):
    if not label or not base_action: return base_action
    
    label_lower = label.lower()
    found_color = ""
    found_object = ""
    found_item = ""
    found_state = ""
    
    # 1. Détection Couleur
    for color, c_suffix in VARIANTS_MAP.items():
        if re.search(r'\b' + re.escape(color) + r'\b', label_lower):
            found_color = c_suffix
            break
            
    # 2. Détection Objet (Blocs, Moniteurs, Capsules, etc.)
    for obj, o_suffix in OBJECT_MAP.items():
        if re.search(r'\b' + re.escape(obj) + r's?\b', label_lower):
            found_object = o_suffix
            break

    # 3. Détection Item (Nouveau V11.11)
    for item, i_suffix in ITEM_MAP.items():
        if re.search(r'\b' + re.escape(item) + r'\b', label_lower):
            found_item = i_suffix
            break

    # 4. Détection État/Transformation (Nouveau V11.14)
    for state, s_suffix in STATE_MAP.items():
        if re.search(r'\b' + re.escape(state) + r'\b', label_lower):
            found_state = s_suffix
            break
            
    # Composition du Suffixe (Priorité: Couleur/Objet > Item > État)
    suffix = found_color or found_object or found_item or found_state
    if suffix:
        if found_color and found_object:
            return f"{base_action}_{found_color}{found_object}"
        return f"{base_action}_{suffix}"
            
    return base_action

def get_action_for_label(label):
    if not label: return None
    label_lower = label.lower()
    
    # Debug: Classification master check
    for action, pattern_regex in parser_dict.ACTION_KEYWORDS.items():
        if re.search(pattern_regex, label_lower):
            return get_action_variant(label, action)
            
    # 2. Inférence Dynamique GREEDY (Fallback Intelligent)
    for pattern, action_prefix in DYNAMIC_PATTERNS:
        match = re.search(pattern, label_lower)
        if match:
            return action_prefix
            
    return None

def get_standardized_desc(action, original_clean_desc):
    if not action: return original_clean_desc
    # Gestion des variantes composites dans la description (ex: OBJECT_INTERACTION_REDBLOCK -> World interaction (Redblock))
    if "_" in action:
        parts = action.split("_")
        base_act = "_".join(parts[:-1])
        variant = parts[-1].lower().capitalize()
        if base_act in STANDARDIZED_DESCRIPTIONS:
            return f"{STANDARDIZED_DESCRIPTIONS[base_act]} ({variant})"
            
    # Si on a une description mappée pour l'action, on l'utilise
    if action in STANDARDIZED_DESCRIPTIONS:
        return STANDARDIZED_DESCRIPTIONS[action]
    
    # Fallback pour les actions dynamiques (ex: DYNAMIC_INFINITE)
    if action and action.startswith("DYNAMIC_"):
        return original_clean_desc
        
    return original_clean_desc

def get_best_condition(action, label=""):
    inc_actions = ["GAIN_LIFE", "COIN_GAIN", "FUNDS_GAINED", "HEAL", "POWERUP_GET", "WEAPON_UPGRADE", "LEVEL_CLEAR", "TITLE_SCREEN", "DYNAMIC_INFINITE", "DYNAMIC_MAX", "DYNAMIC_START"]
    dec_actions = ["LOSE_LIFE", "COIN_LOSE", "FUNDS_SPENT", "TIMER_LOW_WARN", "RESOURCE_USED", "DROWNING", "DYNAMIC_ZERO"]
    
    # Détection des compteurs/timers (V11.24) -> Toujours increase pour éviter le spam
    label_lower = label.lower() if label else ""
    spam_keywords = ["timer", "counter", "duration", "flash", "clignotement", "protection", "frame", "invuln", "damage", "invincibility", "invulnerability"]
    if any(k in label_lower for k in spam_keywords):
        return "increase"
    
    if action in inc_actions: return "increase"
    if action in dec_actions: return "decrease"
    return "change"

def extract_bit_info(desc):
    if not desc: return None, None
    match = re.search(r"\bBit\s*([0-7])\b", desc, re.IGNORECASE)
    if match:
        idx = int(match.group(1))
        return idx, (1 << idx)
    return None, None

def process_mapping(game_id, addr, values_list, flags_list=None):
    OVER_MAP = parser_dict.GAME_SPECIFIC_OVERRIDES
    v_map = {int(v["key"].upper().replace("0X", ""), 16): v["label"] for v in values_list if "key" in v}
    v_action_map = {}
    for v in values_list:
        v_key = v.get("key")
        v_label = v.get("label")
        if v_key is not None and v_label:
            try:
                v_int = int(v_key.upper().replace("0X", ""), 16) if (isinstance(v_key, str) and (v_key.startswith("0x") or v_key.startswith("0X"))) else int(v_key)
                act = get_action_for_label(v_label)
                if act: v_action_map[v_int] = act
            except: continue

    v_bit_map = {}
    metadata = {}
    addr_hex = hex(addr).lower() if isinstance(addr, int) else str(addr)
    game_overrides = OVER_MAP.get(game_id, {}).get(addr_hex, {})
    if isinstance(game_overrides, dict):
        if "force_type" in game_overrides: metadata["force_type"] = game_overrides["force_type"]
        if "force_desc" in game_overrides: metadata["force_desc"] = game_overrides["force_desc"]
    if flags_list:
        for f in flags_list:
            b_idx = f.get("bit_index")
            label = f.get("label", "")
            if b_idx is not None:
                act = get_action_for_label(label)
                if act: v_bit_map[b_idx] = act
    return v_map, v_action_map, metadata, v_bit_map

def get_action_from_mapping(mapping_values):
    if not mapping_values: return None
    # mapping_values is a dict { "1": "Label 1", ... }
    scores = {}
    for val_label in mapping_values.values():
        if not val_label: continue
        act = get_action_for_label(val_label)
        if act:
            scores[act] = scores.get(act, 0) + 1
    
    if not scores: return None
    # Retourne l'action la plus Fréquente dans les labels du mapping (Vote Majoritaire)
    best_act = max(scores, key=scores.get)
    if scores[best_act] >= 1: # Suffisant pour une inférence
        return best_act
    return None
