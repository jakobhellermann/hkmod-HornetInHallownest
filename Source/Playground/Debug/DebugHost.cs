extern alias Silksong;
using System;
using System.Globalization;
using System.Linq;
using HornetInHallownest.DevServer;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using HornetInHallownest.Validation;
using HornetInHallownest.Validation.Scenarios;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Playground;

// The debug server with the mod's routes, and the diagnostic hooks.
internal static class DebugHost {
    private static readonly int Port =
        int.TryParse(Environment.GetEnvironmentVariable("HIH_DEBUG_PORT"), out var port) ? port : 8201;

    private static readonly ValidationRunner validation = new ValidationRunner().Register(new SpawnSanityScenario());
    private static GameObject? go;

    // HkmpHotReload has to precede the HKMP addon.
    internal static void Install() {
        go = new GameObject("HornetInHallownest.Debug");
        Object.DontDestroyOnLoad(go);
        var pump = go.AddComponent<Pump>();

        PlaygroundRoutes.Register();
        RegisterRoutes();
        DebugServer.Start(pump, Port);

        DebugKeybinds.Install();
#if HK1578
        HkmpHotReload.Install();
#endif
        InputDebug.Install();
        FsmTracer.Install();
        HkFsmTracer.Install();
        HkInputTracer.Install();
        PlayMakerWarningContext.Install();
    }

    internal static void Cleanup() {
        DebugKeybinds.Cleanup();
#if HK1578
        HkmpHotReload.Cleanup();
#endif
        InputDebug.Cleanup();
        FsmTracer.Cleanup();
        HkFsmTracer.Cleanup();
        HkInputTracer.Cleanup();
        TransitionRecorder.Cleanup();
        PlayMakerWarningContext.Cleanup();
        DebugServer.Stop();
        if (go != null) Object.Destroy(go);
        go = null;
    }

    private sealed class Pump : MonoBehaviour {
        private void Update() => DebugServer.Update();
    }

    private static void RegisterRoutes() {
        DebugServer.MapPost("/spawn-real", _ => {
            HornetSpawner.Spawn();
            return new { ok = true };
        });
        DebugServer.MapPost("/despawn-real", _ => {
            HornetSpawner.Despawn();
            return new { ok = true };
        });
        DebugServer.MapGet("/hero-state", _ => BundleSpike.HeroState());
        DebugServer.MapPost("/dbg-recoil",
            req => {
                var hero = HornetSpawner.Hornet;
                if (hero == null)
                    return new {
                        error = "no hero"
                    };
                var kind = req["kind"] ?? "bounce";
                if (kind == "dash") hero.sprintFSM?.SendEvent("DASH RECOIL");
                else hero.DownspikeBounce(true);
                return new {
                    fired = kind
                };
            });
        DebugServer.MapGet("/equip-crest",
            req => {
                var id = req["id"] ?? "";
                Silksong::ToolItemManager.SetEquippedCrest(id);
                Silksong::ToolItemManager.SendEquippedChangedEvent(true);
                var hero = HornetSpawner.Hornet;
                hero?.ResetAllCrestState();
                return new {
                    equipped = Silksong::PlayerData.instance.CurrentCrestID,
                    crestConfigSet = hero != null && hero.GetFieldValue<object>("crestConfig") != null,
                    recoilHorVelocity = hero != null ? hero.RECOIL_HOR_VELOCITY : -1f
                };
            });
        DebugServer.MapPost("/equip-tool",
            req => {
                var tool = Silksong::ToolItemManager.GetToolByName(req["name"] ?? "");
                if (!tool)
                    return new {
                        error = $"unknown tool '{req["name"]}'",
                        known = Silksong::ToolItemManager.GetAllTools().Select(t => t.name).ToArray()
                    };

                tool.Unlock(popupFlags: Silksong::ToolItem.PopupFlags.None);
                Silksong::ToolItemManager.AutoEquip(tool);
                return new {
                    equipped = tool.name,
                    crest = Silksong::PlayerData.instance.CurrentCrestID
                };
            });
        // Opens the inventory on that pane (Inv, Tools, Quests, Journal, Map), as it does after a pickup.
        DebugServer.MapPost("/open-inventory",
            req => {
                Silksong::InventoryPaneList.SetNextOpen(req["pane"] ?? "Inv");
                InputDebug.Press("openinventory", 5);
                return new { pane = req["pane"] ?? "Inv" };
            });
        DebugServer.MapPost("/set-pd",
            req => {
                var pd = Silksong::PlayerData.instance;
                var name = req["field"] ?? "";
                var fi = typeof(Silksong::PlayerData).GetField(name);
                if (fi == null)
                    return new {
                        error = $"field '{name}' not found"
                    };
                var val = req["value"] ?? "";
                var t = fi.FieldType;
                object parsed;
                if (t == typeof(bool)) parsed = val.ToLowerInvariant() == "true";
                else if (t == typeof(int)) parsed = int.Parse(val, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) parsed = float.Parse(val, CultureInfo.InvariantCulture);
                else if (t == typeof(string)) parsed = val;
                else
                    return new {
                        error = $"unsupported type {t.Name}"
                    };
                var old = fi.GetValue(pd);
                // Silk goes through the hero so the spool keeps up; a direct write leaves its chunks behind (SilkChunk NREs).
                if (name == "silk" && HornetSpawner.Hornet is { } hero) {
                    var delta = (int)parsed - pd.silk;
                    if (delta > 0) hero.AddSilk(delta, false);
                    else if (delta < 0) hero.TakeSilk(-delta);
                }
                else {
                    fi.SetValue(pd, parsed);
                }
                return new {
                    field = name,
                    old = old?.ToString(),
                    set = fi.GetValue(pd)?.ToString()
                };
            });
        DebugServer.MapPost("/hk-set-pd",
            req => {
                // Through SetBool/SetInt, so the game's hooks see it.
                var pd = PlayerData.instance;
                var name = req["field"] ?? "";
                var fi = typeof(PlayerData).GetField(name);
                if (fi == null)
                    return new {
                        error = $"field '{name}' not found"
                    };
                var val = req["value"] ?? "";
                var old = fi.GetValue(pd);
                if (fi.FieldType == typeof(bool)) pd.SetBool(name, val.ToLowerInvariant() == "true");
                else if (fi.FieldType == typeof(int)) pd.SetInt(name, int.Parse(val, CultureInfo.InvariantCulture));
                else
                    return new {
                        error = $"unsupported type {fi.FieldType.Name}"
                    };
                return new {
                    field = name,
                    old = old?.ToString(),
                    set = fi.GetValue(pd)?.ToString()
                };
            });
        DebugServer.MapPost("/hk-fsm-set-state",
            req => {
                var name = req["fsm"] ?? "";
                var go = req["go"];
                var target = UnityApi.FindAll<PlayMakerFSM>()
                    .Where(f => f.FsmName == name && (go == null || f.gameObject.name == go)).ToList();
                if (target.Count != 1) {
                    var named = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Where(f => f.FsmName == name && f.gameObject.scene.IsValid());
                    return new {
                        error = $"{target.Count} active FSMs named '{name}'" + (go == null ? "" : $" on '{go}'"),
                        candidates = named.Select(f => $"{f.gameObject.scene.name}/{f.gameObject.name} active={f.gameObject.activeInHierarchy}")
                    };
                }
                var from = target[0].ActiveStateName;
                target[0].SetState(req["state"]);
                return new {
                    go = target[0].gameObject.name,
                    from,
                    to = target[0].ActiveStateName
                };
            });
        DebugServer.MapGet("/fsm-dump", req => BundleSpike.FsmDump(req["name"] ?? "Sprint"));
        DebugServer.MapGet("/fsm-dump-any", req => BundleSpike.FsmDumpAny(req["name"] ?? "health_display"));
        DebugServer.MapGet("/fsm-dump-hk", req => BundleSpike.FsmDumpHk(req["name"] ?? "Bell Control"));
        DebugServer.MapPost("/fsm-event",
            req => BundleSpike.SendFsmEvent(req["name"] ?? "health_display", req["event"] ?? "HUD APPEAR RESET"));
        DebugServer.MapGet("/fsm-list", req => BundleSpike.FsmList(req["path"] ?? "Hud Canvas"));
        DebugServer.MapPost("/eval-cs", req => EvalCs.Run(req.Body));
        DebugServer.MapGet("/find-event-senders", req => BundleSpike.FindEventSenders(req["event"] ?? "SHOW HP"));
        DebugServer.MapGet("/fsm-state-actions",
            req => BundleSpike.DumpStateActions(req["name"] ?? "health_display",
                req["state"] ?? "First Pause",
                req["go"]));
        DebugServer.MapGet("/fsm-vars", req => BundleSpike.FsmVars(req["name"] ?? "Bind", req["go"]));
        DebugServer.MapGet("/fsm-vars-hk", req => BundleSpike.FsmVarsHk(req["name"] ?? "Fade", req["go"]));
        DebugServer.MapPost("/grant-kit", _ => PlayerDataSyncModule.GrantFullKit());
        DebugServer.MapGet("/find-fsm-action", req => BundleSpike.FindFsmAction(req["needle"] ?? "SetSprint"));
        DebugServer.MapPost("/fsm-trace", req => FsmTracer.SetTargets(req["names"]));
        DebugServer.MapPost("/hk-fsm-trace", req => HkFsmTracer.SetTargets(req["names"]));
        DebugServer.MapPost("/transition-rec", req => TransitionRecorder.SetEnabled(req["on"] == "true"));
        DebugServer.MapPost("/transition-dump", _ => TransitionRecorder.DumpRoute());
        DebugServer.MapGet("/hero-clips", req => BundleSpike.ListHeroClips(req["filter"]));
        DebugServer.MapPost("/play-clip", req => BundleSpike.PlayHeroClip(req["name"]));
        DebugServer.MapPost("/hero-anim-resume", _ => BundleSpike.ResumeHeroAnim());
        DebugServer.MapPost("/popup", req => DebugPopup.Show(req["text"] ?? ""));
        DebugServer.MapPost("/popup-clear", _ => DebugPopup.Clear());
        DebugServer.MapPost("/load-save",
            req => {
                var slot = int.TryParse(req["slot"], out var s) ? s : 0;
                GameManager.instance.LoadGameFromUI(slot);
                return new {
                    ok = true,
                    slot
                };
            });
        DebugServer.MapPost("/kill", _ => DeathModule.Kill());
        DebugServer.MapPost("/getup", _ => DeathModule.ForceGetUp());
        // 2 spikes, 3 acid, 4 lava, 5 pit
        DebugServer.MapPost("/hazard", req => DeathModule.Hazard(req["type"] ?? "3"));
        DebugServer.MapPost("/validate", (req, respond) => validation.RunRoute(req, respond));
        DebugServer.MapGet("/validate-list", _ => validation.List());
        DebugServer.MapGet("/respawn-state",
            _ => {
                var hk = GameManager.instance.playerData;
                var ss = Silksong::PlayerData.HasInstance ? Silksong::PlayerData.instance : null;
                return new {
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    hk = new {
                        hk.respawnScene,
                        hk.respawnMarkerName,
                        hk.respawnType,
                        hk.atBench
                    },
                    ss = ss != null
                        ? new {
                            ss.respawnScene,
                            ss.respawnMarkerName,
                            ss.respawnType,
                            ss.atBench
                        }
                        : null
                };
            });
        DebugServer.MapPost("/mirror-respawn",
            _ => {
                // Repairs saves from before HK's respawn was kept in sync.
                var ss = Silksong::PlayerData.HasInstance ? Silksong::PlayerData.instance : null;
                var knight = HeroController.UnsafeInstance;
                if (ss == null || knight == null)
                    return new {
                        error = "no Silksong PD / HK hero"
                    };
                knight.SetBenchRespawn(ss.respawnMarkerName, ss.respawnScene, ss.respawnType, false);
                return new {
                    mirrored = new {
                        ss.respawnScene,
                        ss.respawnMarkerName,
                        ss.respawnType
                    }
                };
            });
        DebugServer.MapPost("/switch",
            req => {
                var who = (req["who"] ?? "").ToLowerInvariant();
                return who switch {
                    "knight" => HeroSwitch.SetActive(ActiveHero.Knight),
                    "hornet" => HeroSwitch.SetActive(ActiveHero.Hornet),
                    _ => HeroSwitch.Toggle()
                };
            });
        DebugServer.MapPost("/warp",
            req => {
                var scene = req["scene"];
                if (string.IsNullOrEmpty(scene))
                    return new {
                        error = "scene required (e.g. /warp?scene=RestingGrounds_04&x=46.25&y=7.57)"
                    };
                return DebugWarp.Warp(scene!, DebugWarp.ParseFloat(req["x"]), DebugWarp.ParseFloat(req["y"]));
            });
        DebugServer.MapGet("/render-info",
            req => {
                var go = ComponentPath.Parse(req["path"] ?? "").ResolveGameObject();
                if (!go) return new { error = $"not found: {req["path"]}" };
                return go!.GetComponentsInChildren<Renderer>(true)
                    .Take(int.TryParse(req["limit"], out var limit) ? limit : 20)
                    .Select(r => {
                        var m = r.sharedMaterial;
                        string? Prop(string name) => m && m.HasProperty(name)
                            ? (m.GetTexture(name) is { } t && name.EndsWith("Tex") ? t.name : m.GetColor(name).ToString())
                            : null;
                        return new {
                            path = r.transform.name, r.enabled, layer = r.sortingLayerName, r.sortingLayerID, r.sortingOrder,
                            material = m ? m.name : null, shader = m ? m.shader.name : null,
                            supported = m && m.shader.isSupported, queue = m ? m.renderQueue : -1,
                            color = Prop("_Color"), faceColor = Prop("_FaceColor"), mainTex = Prop("_MainTex"),
                            spriteColor = (r as SpriteRenderer)?.color.ToString(), z = r.transform.position.z
                        };
                    }).ToArray();
            });
        DebugServer.MapGet("/input-bindings",
            _ => {
                static object Describe(InControl.PlayerAction a) => new {
                    pressed = a.IsPressed,
                    value = a.Value,
                    bindings = a.Bindings.Select(b => $"{b.BindingSourceType}:{b.Name}").ToArray()
                };
                var hk = InputHandler.Instance.inputActions;
                var ss = Bootstrap.SilksongBootstrap.InputActions!;
                return new {
                    hkLastInput = $"{hk.LastInputType}/{hk.LastDeviceClass}",
                    activeDevice = InControl.InputManager.ActiveDevice.Name,
                    devices = InControl.InputManager.Devices.Select(d => new {
                        d.Name, d.Meta, d.DeviceClass,
                        activeControls = d.Controls.Where(c => c != null && c.Value != 0f)
                            .Select(c => $"{c.Target}={c.Value:F2}").ToArray()
                    }).ToArray(),
                    hk = typeof(HeroActions).GetFields()
                        .Where(f => typeof(InControl.PlayerAction).IsAssignableFrom(f.FieldType))
                        .ToDictionary(f => f.Name, f => Describe((InControl.PlayerAction)f.GetValue(hk))),
                    hornet = new {
                        dash = new { pressed = ss.Dash.IsPressed, ss.Dash.Value },
                        harpoon = new { pressed = ss.SuperDash.IsPressed, ss.SuperDash.Value },
                        jump = new { pressed = ss.Jump.IsPressed, ss.Jump.Value }
                    }
                };
            });
        DebugServer.MapPost("/press",
            req => {
                // Held for `ms` of wall clock or `frames` (default 60).
                var a = (req["a"] ?? req["action"])?.ToLowerInvariant();
                // The hero-switch bind is HK input, not a Silksong action.
                if (a == "switch") {
                    HeroSwitch.PressSwitchBind();
                    return new { action = a, active = HeroSwitch.Active.ToString() };
                }
                if (a == null || !InputDebug.IsKnownAction(a))
                    return new {
                        error = $"unknown action '{a}'; known: switch,{string.Join(",", InputDebug.KnownActions)}"
                    };
                if (int.TryParse(req["ms"], out var ms) && ms > 0) {
                    InputDebug.PressFor(a, ms / 1000f);
                    return new {
                        action = a,
                        ms
                    };
                }
                if (!int.TryParse(req["frames"], out var f) || f <= 0) f = 60;
                InputDebug.Press(a, f);
                return new {
                    action = a,
                    frames = f
                };
            });
        DebugServer.MapPost("/log-once",
            req => {
                Util.Log.DedupOnce = (req["dedup"] ?? "true").ToLowerInvariant() != "false";
                if (Util.Log.DedupOnce) Util.Log.ClearOnce();
                return new {
                    dedup = Util.Log.DedupOnce
                };
            });
    }
}
