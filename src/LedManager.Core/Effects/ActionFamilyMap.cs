namespace LedManager.Core.Effects;

/// <summary>
/// Fallback action -> famille V11 pour les events ingame dont le payload ne
/// porte pas de famille (anciens wrappers RetroArch, sources tierces).
/// Table alignee sur la nomenclature du generateur mem-curator (APIExpose).
/// </summary>
public static class ActionFamilyMap
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TITLE_SCREEN"] = "flow.lifecycle",
        ["GAME_PLAYING"] = "flow.lifecycle",
        ["GAMEPLAY"] = "flow.lifecycle",
        ["GAME_OVER"] = "flow.lifecycle",
        ["PAUSE_ON"] = "flow.lifecycle",
        ["PAUSE_OFF"] = "flow.lifecycle",
        ["PAUSED"] = "flow.lifecycle",
        ["DEMO_MODE"] = "flow.lifecycle",
        ["CONTINUE_SCREEN"] = "flow.lifecycle",
        ["CONTINUE"] = "flow.lifecycle",
        ["CORPORATE_SCREEN"] = "flow.lifecycle",
        ["CREDITS_SCREEN"] = "flow.lifecycle",
        ["CREDITS"] = "flow.lifecycle",
        ["INTRO_SCREEN"] = "flow.lifecycle",
        ["SELECT_SCREEN"] = "flow.lifecycle",
        ["WORLD_MAP"] = "flow.lifecycle",
        ["LOADING_SCREEN"] = "flow.lifecycle",
        ["SETTINGS_CHANGED"] = "flow.settings",
        ["MODE_STATE"] = "flow.settings",
        ["EVENT_TRIGGER"] = "flow.events",
        ["BALL_LOCK"] = "flow.events",
        ["MULTIBALL_START"] = "flow.events",
        ["COMBO_HIT"] = "flow.events",
        ["NEW_LEVEL"] = "progression.level",
        ["LEVEL_CLEAR"] = "progression.level",
        ["PROGRESSION_ZONE"] = "progression.zone",
        ["PROGRESSION_STAGE"] = "progression.stage",
        ["STAGE_SELECT"] = "progression.stage",
        ["LAP_STATE"] = "progression.stage",
        ["LAP_COMPLETE"] = "progression.stage",
        ["RANK_STATE"] = "progression.stage",
        ["LIVES_STATE"] = "resources.lives",
        ["LOSE_LIFE"] = "resources.lives",
        ["GAIN_LIFE"] = "resources.lives",
        ["1UP"] = "resources.lives",
        ["DEAD"] = "resources.lives",
        ["UNIT_COUNT"] = "resources.lives",
        ["UNIT_GAIN"] = "resources.lives",
        ["UNIT_LOSE"] = "resources.lives",
        ["HEALTH_STATE"] = "resources.health",
        ["HIT"] = "resources.health",
        ["HEAL"] = "resources.health",
        ["LOSE_HEALTH"] = "resources.health",
        ["GAIN_HEALTH"] = "resources.health",
        ["LOW_HEALTH_WARN"] = "resources.health",
        ["DROWNING"] = "resources.environmental",
        ["RESOURCE_STATE"] = "resources.secondary",
        ["RESOURCE_GAIN"] = "resources.secondary",
        ["RESOURCE_LOSE"] = "resources.secondary",
        ["INVENTORY_ITEM"] = "inventory.items",
        ["DYNAMIC_INVENTORY"] = "inventory.items",
        ["ITEM_GET"] = "inventory.items",
        ["ITEM_USE"] = "inventory.items",
        ["TREASURE"] = "inventory.items",
        ["KEY_GET"] = "inventory.items",
        ["CHEST_OPENED"] = "inventory.items",
        ["WEAPON_UPGRADE"] = "inventory.weapon",
        ["WEAPON_STATE"] = "inventory.weapon",
        // Le desc de celle-ci porte un NOM d'arme ("Fire Water") et non une
        // description : de quoi allumer une couleur propre a chaque arme.
        ["WEAPON_SELECTED"] = "inventory.weapon",
        ["SCORE_STATE"] = "scoring.points",
        ["SCORE"] = "scoring.points",
        ["COIN_GAIN"] = "scoring.collectibles",
        ["COIN_LOSE"] = "scoring.collectibles",
        ["RING_GAIN"] = "scoring.collectibles",
        ["RING_LOSE"] = "scoring.collectibles",
        ["MONEY_STATE"] = "scoring.collectibles",
        ["FUNDS_GAINED"] = "scoring.collectibles",
        ["FUNDS_SPENT"] = "scoring.collectibles",
        ["AMMO_STATE"] = "scoring.collectibles",
        ["AMMO_GAIN"] = "scoring.collectibles",
        ["AMMO_LOSE"] = "scoring.collectibles",
        ["EXPERIENCE_STATE"] = "scoring.experience",
        ["BOSS_HIT"] = "combat.enemies",
        ["BOSS_HEAL"] = "combat.enemies",
        ["BOSS_DEFEATED"] = "combat.enemies",
        ["ENEMY_HIT"] = "combat.enemies",
        ["BOMB_FIRED"] = "combat.enemies",
        ["FIRE_SIDEARM"] = "combat.enemies",
        ["BATTLE_START"] = "combat.tactical",
        ["BATTLE_END"] = "combat.tactical",
        ["CRITICAL_HIT"] = "combat.tactical",
        ["PARRY_SUCCESS"] = "combat.tactical",
        ["KO"] = "combat.tactical",
        ["FATALITY"] = "combat.tactical",
        ["CRASH"] = "racing.vehicle",
        ["COLLISION"] = "racing.vehicle",
        ["TURBO_BOOST"] = "racing.vehicle",
        ["GEAR_SHIFT"] = "racing.vehicle",
        ["INVINCIBILITY_START"] = "state.temporary",
        ["INVINCIBILITY_STOP"] = "state.temporary",
        ["INVINCIBILITY_TIMER"] = "state.temporary",
        ["SPEED_START"] = "state.temporary",
        ["SPEED_STOP"] = "state.temporary",
        ["SPEED_TIMER"] = "state.temporary",
        ["SHIELD_GAIN"] = "state.temporary",
        ["SHIELD_LOST"] = "state.temporary",
        ["SHIELD_TIMER"] = "state.temporary",
        ["STATUS_EFFECT_START"] = "state.temporary",
        ["STATUS_EFFECT_STOP"] = "state.temporary",
        ["TRANSFORMATION"] = "state.temporary",
        ["SPECIAL_ACTION"] = "state.temporary",
        ["JUMPING"] = "state.player",
        ["RUNNING"] = "state.player",
        ["CROUCHING"] = "state.player",
        ["FALLING"] = "state.player",
        ["SPINNING"] = "state.player",
        ["SWIMMING"] = "state.player",
        ["ATTACKING"] = "state.player",
        // Idem : le desc nomme le personnage joue, pour ce joueur-la.
        ["CHARACTER_SELECTED"] = "state.player",
        ["MOUNT_START"] = "state.mount",
        ["MOUNT_STOP"] = "state.mount",
        ["MOUNT_STATE"] = "state.mount",
        ["OBJECT_INTERACTION"] = "world_interaction.objects",
        ["OBJECT_DESTROYED"] = "world_interaction.objects",
        ["DOOR_OPENED"] = "world_interaction.objects",
        ["ROOM_DISCOVERED"] = "world_interaction.objects",
        ["SECRET_REVEALED"] = "world_interaction.objects",
        ["ENVIRONMENT_FORCE"] = "world_interaction.objects",
        ["SPEED_STATE"] = "system.movement",
        ["GENERAL_TIMER"] = "system.timer",
        ["LEVEL_TIMER"] = "system.timer",
        ["BOMB_TIMER"] = "system.timer",
        ["TIMER_LOW_WARN"] = "system.timer",
        ["TIMER_LOW"] = "system.timer",
        ["COMBO_TIMER"] = "system.timer",
    };

    private static readonly (string Prefix, string Family)[] Prefixes =
    {
        ("LEVEL_", "progression.level"),
        ("TREASURE_", "inventory.items"),
        ("TRANSFORMATION_", "state.temporary"),
        ("PLAYER_STATE_", "state.temporary"),
        ("OBJECT_INTERACTION_", "world_interaction.objects"),
        ("ENVIRONMENT_", "world_interaction.objects"),
    };

    /// <summary>Retourne la famille V11 d'une action, ou "" si inconnue.</summary>
    public static string Resolve(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "";
        }

        var key = action.Trim();
        if (Exact.TryGetValue(key, out var family))
        {
            return family;
        }

        foreach (var (prefix, prefixFamily) in Prefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && key.Length > prefix.Length)
            {
                return prefixFamily;
            }
        }

        return "";
    }
}
