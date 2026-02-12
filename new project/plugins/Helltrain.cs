using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using Oxide.Core;
using Rust;
using UnityEngine;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using Facepunch;
using System;
using System.Text;
using System.Reflection;


namespace Oxide.Plugins
{
    [Info("Helltrain", "BLOODHELL", "6.6.6")]
    [Description("Поезд для ивентов с фракциями и лутом")]
    public class Helltrain : RustPlugin
    {
		
		void Unload()
{
    try
    {
        // Сносим наш состав и все наши прикреплённые сущности
        KillEventTrainCars("plugin_unload");

    }
    catch (Exception ex)
    {
        PrintError($"Unload cleanup error: {ex}");
    }
}
		
private const ulong HELL_OWNER_ID = 99999999999999999UL; // любое уникальное число формата ulong
private readonly HashSet<BaseNetworkable> _spawnedTrainEntities = new HashSet<BaseNetworkable>();
		// 🔇 Антиспам по хак-крейту


 private bool _explosionTimerArmedOnce = false;
 private Timer _engineWatchdog;
 private bool _explodedOnce = false;
 // глушилка хуков и анти-дубль очистки по локомотиву
private bool _suppressHooks = false;
private bool _engineCleanupTriggered = false;
private float _engineCleanupCooldownUntil = 0f;
private bool _firstLootAnnounced = false;
private const string LAPTOP_PREFAB_PATH = "assets/prefabs/misc/laptop_deployable.prefab";
private void Broadcast(string msg) => Server.Broadcast(msg);

private enum CrateState { Idle, CountingDown, Open }

// === Tracking helpers ===
private void Track(BaseNetworkable ent)
{
    if (ent != null && !ent.IsDestroyed) 
		_spawnedTrainEntities.Add(ent);
}
private void UntrackAndKill(BaseNetworkable ent)
{
    if (ent == null) return;
    _spawnedTrainEntities.Remove(ent);
    if (!ent.IsDestroyed) ent.Kill();
}




		[PluginReference] Plugin KitsSuite;
		[PluginReference]
private Plugin Loottable;

private System.Random _rng = new System.Random();

private string PickPresetAB(string factionUpper)
{
    string a = factionUpper + "_A";
    string b = factionUpper + "_B";
    return (_rng.Next(2) == 0) ? a : b;
}

// === Loottable preset bootstrap ===
private void RegisterHelltrainPresetsToLoottable()
{
    if (Loottable == null)
    {
        PrintWarning("Loottable не найден — пресеты не зарегистрированы");
        return;
    }

    // Очистим наши старые (если были), зададим категорию и создадим 6 пресетов
    Loottable.Call("ClearPresets", this);
    Loottable.Call("CreatePresetCategory", this, "Helltrain");

    // 6 ключей: PMC_A/B, COBLAB_A/B, BANDIT_A/B
    Loottable.Call("CreatePreset", this, "PMC_A", "Helltrain · PMC A", null, false);
    Loottable.Call("CreatePreset", this, "PMC_B", "Helltrain · PMC B", null, false);
    Loottable.Call("CreatePreset", this, "COBLAB_A", "Helltrain · COBLAB A", null, false);
    Loottable.Call("CreatePreset", this, "COBLAB_B", "Helltrain · COBLAB B", null, false);
    Loottable.Call("CreatePreset", this, "BANDIT_A", "Helltrain · BANDIT A", null, false);
    Loottable.Call("CreatePreset", this, "BANDIT_B", "Helltrain · BANDIT B", null, false);

    Puts("[Helltrain] Loottable: зарегистрированы пресеты PMC/COBLAB/BANDIT (A/B).");
}

// Регистрируем пресеты на старте сервера
private void OnServerInitialized()
{
    try { RegisterHelltrainPresetsToLoottable(); } catch { /* no-op */ }
}


	         private TrainEngine activeHellTrain = null;
        private Timer respawnTimer = null;
		private Timer _gridCheckTimer = null;
        private List<TrainTrackSpline> availableOverworldSplines = new List<TrainTrackSpline>();
        private List<TrainTrackSpline> availableUnderworldSplines = new List<TrainTrackSpline>();
		private bool _allowDestroy = false;

        #region HT.PREFABS
        private const string EnginePrefab = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab";
        private const string WorkcartPrefab = "assets/content/vehicles/trains/workcart/workcart.entity.prefab";
        private const string WagonPrefabA = "assets/content/vehicles/trains/wagons/trainwagona.entity.prefab";
        private const string WagonPrefabB = "assets/content/vehicles/trains/wagons/trainwagonb.entity.prefab";
        private const string WagonPrefabC = "assets/content/vehicles/trains/wagons/trainwagonc.entity.prefab";
        private const string WagonPrefabLoot = "assets/content/vehicles/trains/wagons/trainwagonunloadableloot.entity.prefab";
        private const string WagonPrefabUnloaded = "assets/content/vehicles/trains/wagons/trainwagonunloadable.entity.prefab";
 private const string PREFAB_CRATE_PMC    = "assets/bundled/prefabs/radtown/crate_elite.prefab";
 private const string PREFAB_CRATE_BANDIT = "assets/bundled/prefabs/radtown/crate_normal_2.prefab";
 private const string PREFAB_CRATE_COBLAB = "assets/bundled/prefabs/radtown/crate_normal.prefab";
        private const string SCIENTIST_PREFAB = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cargo_turret_any.prefab";
        private const string SAMSITE_PREFAB = "assets/prefabs/npc/sam_site_turret/sam_static.prefab";
       private const string TURRET_PREFAB = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string HACKABLE_CRATE_PREFAB = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
       public string HackableCratePrefab => HACKABLE_CRATE_PREFAB;
	   private string GetCratePrefabForFaction(string faction)
{
    switch ((faction ?? "BANDIT").ToUpper())
    {
        case "PMC":    return PREFAB_CRATE_PMC;
        case "COBLAB": return PREFAB_CRATE_COBLAB;
        default:       return PREFAB_CRATE_BANDIT;
    }
}
	  
	  #endregion
	  
		
#region HT.AI.COMPONENTS

public class HellTrainDefender : MonoBehaviour { }

public class TurretMarker : MonoBehaviour
{
    public string gun;
    public string ammo;
    public int ammoCount;

    public void Set(string gun, string ammo, int ammoCount)
    {
        this.gun = gun;
        this.ammo = ammo;
        this.ammoCount = ammoCount;
    }
}



public class NPCTypeMarker : MonoBehaviour
{
	public string savedKit;
public List<string> savedKits = new List<string>();
    public string npcType;
}

// ✅ ТОЛЬКО ОДИН класс TrainAutoTurret!
public class TrainAutoTurret : MonoBehaviour
{
    private AutoTurret turret;
    private bool weaponReady = false;
    public Helltrain plugin;
    
    void Start()
    {
        turret = GetComponent<AutoTurret>();
        if (turret == null) return;
        
        gameObject.AddComponent<HellTrainDefender>();
        
        turret.SetFlag(IOEntity.Flag_HasPower, true, false, true);
        turret.UpdateFromInput(100, 0);
        
        turret.SetFlag(BaseEntity.Flags.On, false, false, true);
        turret.isLootable = false;
        turret.sightRange = 30f;
        
        turret.InvokeRepeating(CheckTargetForFF, 0.5f, 0.5f);
        turret.InvokeRepeating(CheckMagazine, 0.5f, 0.5f);
        turret.InvokeRepeating(RefillAmmo, 5f, 5f);
    }
    
	
    private void CheckMagazine()
    {
        if (turret == null || turret.IsDestroyed || turret.inventory == null) 
            return;
        
        if (!turret.HasFlag(IOEntity.Flag_HasPower))
        {
            turret.SetFlag(IOEntity.Flag_HasPower, true, false, true);
            turret.UpdateFromInput(100, 0);
        }
        
        if (!weaponReady)
        {
            if (turret.inventory.itemList.Count >= 2)
            {
                weaponReady = true;
                
                turret.SetFlag(IOEntity.Flag_HasPower, true, false, true);
                turret.UpdateFromInput(100, 0);
                
                turret.UpdateAttachedWeapon();
                turret.UpdateTotalAmmo();
                turret.SetFlag(BaseEntity.Flags.On, true, false, true);
                
                turret.SendNetworkUpdate();
                
                if (plugin != null)
                    plugin.Puts($"   🔋 Турель получила питание и включена!");
            }
            return;
        }
        
        if (turret.inventory.itemList.Count > 0)
        {
            Item weaponItem = turret.inventory.itemList[0];
            if (weaponItem != null)
            {
                BaseProjectile weapon = weaponItem.GetHeldEntity() as BaseProjectile;
                if (weapon != null && weapon.primaryMagazine != null)
                {
                    if (weapon.primaryMagazine.contents == 0)
                    {
                        weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity;
                        weapon.SendNetworkUpdateImmediate();
                    }
                }
            }
        }
    }
    
    private void RefillAmmo()
    {
        if (turret == null || turret.IsDestroyed || turret.inventory == null) 
            return;
        
        if (turret.inventory.itemList.Count > 1)
        {
            Item ammoItem = turret.inventory.itemList[1];
            if (ammoItem != null && ammoItem.amount < 500)
            {
                ammoItem.amount = 500;
                ammoItem.MarkDirty();
                turret.UpdateTotalAmmo();
            }
        }
    }
    
    private void CheckTargetForFF()
    {
        if (turret == null || turret.IsDestroyed) return;
        
        if (turret.target != null)
        {
            var targetDefender = turret.target.GetComponent<HellTrainDefender>();
            if (targetDefender != null)
            {
                turret.SetTarget(null);
            }
        }
    }
    
    void OnDestroy()
    {
        if (turret != null && !turret.IsDestroyed)
        {
            CancelInvoke("CheckTargetForFF");
            CancelInvoke("CheckMagazine");
            CancelInvoke("RefillAmmo");
        }
    }
}

public class TrainSamSite : MonoBehaviour
{
    private SamSite samsite;
    
    void Awake()
    {
        samsite = GetComponent<SamSite>();
        if (samsite == null) return;
        
        samsite.staticRespawn = true;
        gameObject.AddComponent<HellTrainDefender>();
    }
}

public class HellTrainComponent : MonoBehaviour
{
    public Helltrain plugin;
    public TrainEngine engine;
    private int zeroSpeedTicks = 0;
    private bool movingForward = true;

    private void FixedUpdate()
    {
        if (engine == null || engine.IsDestroyed) 
        {
            Destroy(this);
            return;
        }

        float speed = engine.GetTrackSpeed();

        if (Mathf.Abs(speed) < 0.1f)
        {
            zeroSpeedTicks++;

            if (zeroSpeedTicks >= 90)
            {
                movingForward = !movingForward;

                if (movingForward)
                    engine.SetThrottle(TrainEngine.EngineSpeeds.Fwd_Hi);
                else
                    engine.SetThrottle(TrainEngine.EngineSpeeds.Rev_Hi);

                plugin.Puts($"⚠️ Поезд застрял! Реверс → {(movingForward ? "ВПЕРЁД" : "НАЗАД")}");

                zeroSpeedTicks = 0;
            }
        }
        else
        {
            zeroSpeedTicks = 0;
        }
    }
}

private void StartEngineWatchdog()
{
    _engineWatchdog = timer.Every(5f, () =>
    {
        // если у нас вообще ничего не заспавнено — молчим
        if (_spawnedCars.Count == 0 && _spawnedTrainEntities.Count == 0) return;

        // есть ли среди наших вагонов живой локомотив?
        bool engineAlive = false;
        foreach (var e in _spawnedCars)
        {
            var eng = e as TrainEngine;
            if (eng != null && !eng.IsDestroyed) { engineAlive = true; break; }
        }
        if (!engineAlive)
        {
            Puts("[Helltrain] Engine watchdog: engine missing → cleanup event cars");
            KillEventTrainCars("watchdog_no_engine");
        }
    });
}

private void StopEngineWatchdog()
{
    if (_engineWatchdog != null)
    {
        _engineWatchdog.Destroy();
        _engineWatchdog = null;
    }
}


// Если внешний плагин/команда (cleanup.trains и т.п.) убила наш локомотив,
// автоматически добиваем все ивентовые вагоны, чтобы не оставались "призраки".
// Если внешний плагин/команда убила наш локомотив → чистим состав
private void OnEntityKill(BaseNetworkable entity)
{
    if (_suppressHooks) return;

    var engine = entity as TrainEngine;
    if (engine == null) return;

    // реагируем ТОЛЬКО на наш ивент-лок, если метка есть
    bool ours = (_spawnedCars.Contains(engine) || _spawnedTrainEntities.Contains(engine));
    if (!ours && engine.OwnerID != HELL_OWNER_ID) return;

    // анти-спам: 1 вызов в секунду и только один триггер до конца очистки
    if (Time.realtimeSinceStartup < _engineCleanupCooldownUntil) return;
    _engineCleanupCooldownUntil = Time.realtimeSinceStartup + 1f;
    if (_engineCleanupTriggered) return;
    _engineCleanupTriggered = true;

    Puts("[Helltrain] Engine OnEntityKill → cleanup event cars");
    KillEventTrainCars("engine_removed");
}

private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
{
    if (_suppressHooks) return;

    var engine = entity as TrainEngine;
    if (engine == null) return;

    if (Time.realtimeSinceStartup < _engineCleanupCooldownUntil) return;
    _engineCleanupCooldownUntil = Time.realtimeSinceStartup + 1f;
    if (_engineCleanupTriggered) return;
    _engineCleanupTriggered = true;

    Puts("[Helltrain] Engine OnEntityDeath → cleanup event cars");
    KillEventTrainCars("engine_died");
}



// Хелпер: снести весь наш состав (только ивентовые entities)
private void KillEventTrainCars(string reason)
{
    _suppressHooks = true;
    StopEngineWatchdog();
    StopGridCheckTimer();
    CancelLifecycleTimer();

    try
    {
        // восстановить защиту (если меняли)
        RestoreProtectionForAll();

        // убиваем всё по «снимку», чтобы не падать и не зациклиться на хуках
        var entsSnap = _spawnedTrainEntities.ToArray();
        foreach (var e in entsSnap)
            if (e != null && !e.IsDestroyed) e.Kill();
        _spawnedTrainEntities.Clear();

        var carsSnap = _spawnedCars.ToArray();
        foreach (var car in carsSnap)
            if (car != null && !car.IsDestroyed) car.Kill();
        _spawnedCars.Clear();

        _spawnedTurrets.Clear();
        _spawnedSamSites.Clear();
        _spawnedNPCs.Clear();
        _savedProtection.Clear();

        _explosionDamageArmed = false;
        _explodedOnce = false;
        activeHellTrain = null;
        _trainLifecycle = null;

        Puts($"[Helltrain] Event cars cleanup completed ({reason}).");
    }
    catch (Exception ex)
    {
        PrintError($"KillEventTrainCars error: {ex}");
    }
    finally
    {
        _suppressHooks = false;               // снова слушаем хуки
        _engineCleanupTriggered = false;      // разрешим будущие триггеры
        _engineCleanupCooldownUntil = 0f;
    }
}




#endregion

        #region HT.CONFIG

private class ConfigData
{
	
	[JsonProperty("LootTimerRanges")]
public Dictionary<string, LootTimerRange> LootTimerRanges { get; set; } = new Dictionary<string, LootTimerRange>
{
    ["BANDIT"] = new LootTimerRange { Min = 250, Max = 350 },
    ["COBLAB"] = new LootTimerRange { Min = 300, Max = 425 },
    ["PMC"]    = new LootTimerRange { Min = 400, Max = 500 },
};
public class LootTimerRange { public int Min { get; set; } = 250; public int Max { get; set; } = 500; }

	
	
    public Dictionary<string, TrainComposition> Compositions { get; set; } = new Dictionary<string, TrainComposition>
    {
        ["bandit"] = new TrainComposition
        {
            Tier = TrainTier.LIGHT,
            Weight = 34,
            Wagons = new List<string> { "loco_bandit", "wagonA_bandit", "wagonB_bandit", "wagonC_bandit" }
        },
        ["coblab"] = new TrainComposition
        {
            Tier = TrainTier.MEDIUM,
            Weight = 33,
            Wagons = new List<string> { "loco_coblab", "wagonA_labcob", "wagonA_labcob", "wagonB_labcob", "wagonB_labcob" }
        },
        ["pmc"] = new TrainComposition
        {
            Tier = TrainTier.HEAVY,
            Weight = 33,
            Wagons = new List<string> { "loco_pmc", "wagonC_pmc", "wagonA_pmc", "wagonA_pmc", "wagonB_pmc", "wagonB_pmc", "wagonC_samsite" }
        }
    };
    
    public SpeedSettings Speed { get; set; } = new SpeedSettings();
    
    public bool AutoRespawn { get; set; } = true;
    public float RespawnTime { get; set; } = 60f;
    
    [JsonProperty("Разрешить спавн на поверхности")]
    public bool AllowAboveGround { get; set; } = true;

    [JsonProperty("Разрешить спавн в подземке")]
    public bool AllowUnderGround { get; set; } = false;

    [JsonProperty("Разрешить переходы между уровнями")]
    public bool AllowTransition { get; set; } = false;

    [JsonProperty("Минимальная длина трека для спавна (метры)")]
    public float MinTrackLength { get; set; } = 500f;
    
    [JsonProperty("Названия композиций для анонсов")]
    public Dictionary<string, string> CompositionNames { get; set; } = new Dictionary<string, string>
    {
        ["bandit"] = "Бандитский состав",
        ["coblab"] = "Поезд ученых",
        ["pmc"] = "ЧВК"
    };

    [JsonProperty("Время жизни поезда (минуты)")]
    public float TrainLifetimeMinutes { get; set; } = 60f;

    [JsonProperty("Время респавна после уничтожения (минуты)")]
    public float TrainRespawnMinutes { get; set; } = 60f;

    [JsonProperty("Время до взрыва после взлома (секунды)")]
    public int ExplosionTimerSeconds { get; set; } = 180;

    [JsonProperty("Анонсы времени до взрыва (секунды)")]
    public List<int> ExplosionAnnouncements { get; set; } = new List<int> { 120, 60, 20, 5 };

    [JsonProperty("Количество C4 на вагон при взрыве")]
    public int C4PerWagon { get; set; } = 5;
    
    public Dictionary<string, object> NPC_Types { get; set; } = new Dictionary<string, object>();
    
    // ✅ НОВОЕ: СИСТЕМА АНОНСОВ
    [JsonProperty("Сообщения")]
    public MessageSettings Messages { get; set; } = new MessageSettings();
    
    // ✅ НОВОЕ: ВЕСА
    public class TrainComposition
    {
        public TrainTier Tier { get; set; }
        
        [JsonProperty("Вес (вероятность спавна)")]
        public int Weight { get; set; } = 33;
        
        public List<string> Wagons { get; set; }
    }
    
    public class SpeedSettings
    {
        [JsonProperty("PMC (Heavy) - максимальная скорость")]
        public float TierHeavy { get; set; } = 10f;
        
        [JsonProperty("COBLAB (Medium) - максимальная скорость")]
        public float TierMedium { get; set; } = 12f;
        
        [JsonProperty("Bandit (Light) - максимальная скорость")]
        public float TierLight { get; set; } = 14f;
    }
    
    public enum TrainTier
    {
        LIGHT,
        MEDIUM,
        HEAVY
    }
    
    public class MessageSettings
    {
        [JsonProperty("Спавн поезда")]
        public string TrainSpawned { get; set; } = "🚂 {trainName} появился в квадрате {grid}!";
        
        [JsonProperty("Направление движения")]
        public string TrainDirection { get; set; } = "🚂 {trainName} движется из {fromGrid} → {toGrid}";
        
        [JsonProperty("Взлом начат")]
        public string HackStarted { get; set; } = "🔥 {trainName} ВЗЛОМАН! {minutes} МИНУТ ДО ВЗРЫВА!";
        
        [JsonProperty("Отсчёт взрыва (минуты)")]
        public string ExplosionMinutes { get; set; } = "⚠️ {trainName} взорвётся через {minutes} {minutesWord}!";
        
        [JsonProperty("Отсчёт взрыва (секунды)")]
        public string ExplosionSeconds { get; set; } = "💥 {trainName} взорвётся через {seconds} секунд!";
        
        [JsonProperty("Взрыв")]
        public string Exploded { get; set; } = "💥 {trainName} ВЗОРВАН!";
        
        [JsonProperty("Успешная разгрузка")]
        public string SuccessfulDelivery { get; set; } = "✅ {trainName} успешно разгрузился";
        
        [JsonProperty("Следующий поезд")]
        public string NextTrain { get; set; } = "⏳ Следующий поезд через {minutes} {minutesWord}";
    }
}

// Регистрация пресетов Helltrain в лут-таблице (заглушка, чтобы не падать при компиляции)
// Если потребуется реальная логика — допишем отдельно.


private ConfigData config;

protected override void LoadDefaultConfig()
{
    config = new ConfigData();
    SaveConfig();
}

protected override void LoadConfig()
{
    base.LoadConfig();
    config = Config.ReadObject<ConfigData>();
    SaveConfig();
}

protected override void SaveConfig() => Config.WriteObject(config);

#endregion
		
		#region HT.LIFECYCLE

private class TrainLifecycle
{
    public DateTime SpawnTime;
    public DateTime? FirstLootTime;
    public string LastGrid;
    public bool DirectionAnnounced;
    public string CompositionType; // bandit/coblab/pmc
    
    public TrainLifecycle(string compositionType, Vector3 startPos, Helltrain plugin)
    {
        SpawnTime = DateTime.Now;
        CompositionType = compositionType;
        LastGrid = plugin.GetGridPosition(startPos);
    }
}

private TrainLifecycle _trainLifecycle = null;

#endregion


#region HT.TIMERS

// Таймер жизненного цикла (если поезд не лутали — через lifeMin минут снесём и поставим респавн)

// Остановка таймера проверки грида (без дублей)
private void StopGridCheckTimer()
{
    if (_gridCheckTimer != null)
    {
        _gridCheckTimer.Destroy();
        _gridCheckTimer = null;
    }
}



private void StartLifecycleTimer()
{
    CancelLifecycleTimer();

    float lifeMin = config.TrainLifetimeMinutes; // обычно 60
    _lifecycleTimer = timer.Once(lifeMin * 60f, () =>
    {
        // Никто не лутал — считаем «успешная доставка», сносим состав и готовим респавн
        ForceDestroyHellTrain();
        StartRespawnTimer();
    });

    Puts($"⏰ Lifecycle таймер запущен на {lifeMin} мин.");
}

private void CancelLifecycleTimer()
{
    if (_lifecycleTimer != null)
    {
        _lifecycleTimer.Destroy();
        _lifecycleTimer = null;
        Puts("Lifecycle timer canceled");
    }
}


 // Визуал перед детонацией (огни/звук/дым) — T≈total-15
 private void PlayPreDetonationFx()
{
    foreach (var car in _spawnedCars)
    {
        if (car == null || car.IsDestroyed) continue;

        // Эффект предупреждения (огонь, дым, звук)
        Effect.server.Run(
            "assets/prefabs/misc/fireball/small_explosion.prefab",
            car.transform.position,
            Vector3.up
        );
    }

    Server.Broadcast("⚠️ Поезд дрожит... взрыв близко!");
}

// Периодическая проверка состояния состава/сетки (безопасная заглушка)
private void CheckTrainGrid()
{
    // если поезда нет — ничего не делаем
    if (activeHellTrain == null || _trainLifecycle == null)
        return;

    // при желании можно добавить тут свои проверки (например уход из грида/декора)
    // сейчас просто «пинг», чтобы не падала компиляция
}


// FX взрыва + контролируемый AoE-урон вокруг каждого вагона
private void SpawnExplosionFXAndDamage()
{
    // Проходимся по всем нашим вагонам
    foreach (var car in _spawnedCars)
    {
        if (car == null || car.IsDestroyed) continue;

        // Визуальный эффект взрыва на каждом вагоне
        Effect.server.Run(
            "assets/bundled/prefabs/fx/explosions/explosion_03.prefab",
            car.transform.position,
            Vector3.up
        );

        // AoE-урон по окрестным сущностям (8м радиус)
        var ents = Pool.GetList<BaseCombatEntity>();
        Vis.Entities(car.transform.position, 8f, ents, Rust.Layers.Mask.Default);

        foreach (var e in ents)
        {
            if (e == null || e.IsDestroyed) continue;

            var hi = new HitInfo
            {
                damageTypes = new DamageTypeList()
            };
            hi.damageTypes.Add(DamageType.Explosion, 1000f);
            hi.PointStart = car.transform.position + Vector3.up * 0.5f;

            e.OnAttacked(hi);
        }

        Pool.FreeList(ref ents);
    }
}


private void ArmExplosionDamage()
{
    if (_explosionDamageArmed) return;
    _explosionDamageArmed = true;

    Puts("Explosion damage window ARMED (T-6s)");

    foreach (var car in _spawnedCars)
    {
        if (car == null || car.IsDestroyed) continue;

        var tc = car as TrainCar;
        if (tc == null) continue;

        var id = (uint)(tc.net?.ID.Value ?? 0UL);
if (id == 0U) continue;

        if (!_savedProtection.ContainsKey(id))
            _savedProtection[id] = tc.baseProtection;

        var allow = ScriptableObject.CreateInstance<ProtectionProperties>();
        allow.density = 100;
        allow.amounts = new float[]
        {
            1,1,1,1,1,1,1,1,1,1,
            1,1,1,1,1,1,1,1,1,1,
            1,1,1,1,1
        };

        tc.baseProtection = allow;
    }
}



private void SpawnC4OnTrain()
{
    int perWagon = Mathf.Max(1, config.C4PerWagon);
    float fuse = Mathf.Max(3f, config.ExplosionTimerSeconds);

    Vector3[] offsets = new Vector3[]
    {
        new Vector3(-2f, 0.5f, -2f),
        new Vector3( 2f, 0.5f, -2f),
        new Vector3(-2f, 0.5f,  2f),
        new Vector3( 2f, 0.5f,  2f),
        new Vector3( 0f, 0.5f,  0f)
    };

    foreach (var car in _spawnedCars)
    {
        var tc = car as TrainCar;
        if (tc == null || tc.IsDestroyed) continue;

        for (int i = 0; i < perWagon; i++)
        {
            Vector3 pos = tc.transform.TransformPoint(offsets[i % offsets.Length]);
            var c4 = GameManager.server.CreateEntity("assets/prefabs/tools/c4/explosive.timed.deployed.prefab", pos) as TimedExplosive;
            if (c4 == null) continue;

            c4.timerAmountMax = fuse;
            c4.timerAmountMin = fuse;
            c4.Spawn();
            c4.SetFuse(fuse);
        }
    }

    Puts($"💣 C4 заспавнены ({perWagon} на вагон), взрыв через {fuse:F0} сек...");
}


private void DestroyTrainAfterExplosion()
{
    if (_explodedOnce) return;           // защита от двойного вызова
    _explodedOnce = true;
SpawnExplosionFXAndDamage();
	StopEngineWatchdog();

    string trainName = _trainLifecycle != null
        ? config.CompositionNames[_trainLifecycle.CompositionType]
        : "Hell Train";
		
RestoreProtectionForAll();
    Server.Broadcast(config.Messages.Exploded.Replace("{trainName}", trainName));
    Puts("💥 Взрыв! Диспавн состава...");

    // Снести весь наш состав: все TrainCar, все крейты/NPC/турели/SAM и пр.
	
    try
    {
        // если где-то не все вагоны добавились в _spawnedCars — добьёмся по трекингу
        foreach (var e in _spawnedTrainEntities.ToArray())
        {
            if (e != null && !e.IsDestroyed) e.Kill();
            _spawnedTrainEntities.Remove(e);
        }

        foreach (var car in _spawnedCars.ToArray())
        {
            if (car != null && !car.IsDestroyed) car.Kill();
            _spawnedCars.Remove(car);
        }
    }
    finally
    {
        _spawnedTurrets.Clear();
        _spawnedSamSites.Clear();
        _spawnedNPCs.Clear();
        _savedProtection.Clear();
        _explosionDamageArmed = false;
        activeHellTrain = null;
        _trainLifecycle = null;
    }

    if (config.AutoRespawn)
        StartRespawnTimer();
}


private void StartRespawnTimer()
{
    if (respawnTimer != null)
        respawnTimer.Destroy();
    
    float minutes = config.TrainRespawnMinutes;
    respawnTimer = timer.Once(minutes * 60f, () => SpawnHellTrain());
    
    // ✅ ИЗМЕНЕНО: АНОНС СЛЕДУЮЩЕГО ПОЕЗДА
    string minutesWord = GetMinutesWord((int)minutes);
    string message = config.Messages.NextTrain
        .Replace("{minutes}", minutes.ToString("F0"))
        .Replace("{minutesWord}", minutesWord);
    
    Server.Broadcast(message);
    Puts($"⏳ Респавн через {minutes} минут");
}

#endregion
		

        #region HT.LAYOUT.LOADER
private readonly Dictionary<string, TrainLayout> _layouts = new Dictionary<string, TrainLayout>(System.StringComparer.OrdinalIgnoreCase);
private const string LayoutDir = "Helltrain/Layouts";

private class TrainLayout
{
    [JsonProperty("name")]
    public string name { get; set; }
    
    [JsonProperty("faction")]
    public string faction { get; set; }
    
    [JsonProperty("cars")]
    public List<CarSpec> cars { get; set; }
    
    [JsonProperty("objects")]
    public List<ObjSpec> objects { get; set; }
    
    // ✅ УБРАЛИ ДУБЛИРУЮЩИЕ СВОЙСТВА Name/Faction/Wagons!
    // Они мешали десериализации
}

private class CarSpec
{
    [JsonProperty("type")]
    public string type;
    
    [JsonProperty("variant")]
    public string variant;
    
    // ✅ УБРАЛИ Type/Prefab — они не нужны!
}

private class ObjSpec
{
	
	[JsonIgnore]
public int ammoCount { get => ammo_count; set => ammo_count = value; }

	
    [JsonProperty("type")]
    public string type;
    
    [JsonProperty("faction")]
    public string faction;
    
    [JsonProperty("npc_type")]
    public string npc_type;
    
    [JsonProperty("kit")]
    public string kit;
    
    [JsonProperty("kits")]
    public List<string> kits;
    
    [JsonProperty("gun")]
    public string gun;
    
    [JsonProperty("ammo")]
    public string ammo;
    
    [JsonProperty("ammo_count")]
    public int ammo_count;
    
    [JsonProperty("preset")]
    public string preset;
    
    [JsonProperty("presets")]
    public string[] presets;
    
    [JsonProperty("position")]
    public float[] position;
    
    [JsonProperty("rotationY")]
    public float rotationY;
    
    // ✅ НОВОЕ ПОЛЕ ДЛЯ HP
    [JsonProperty("health")]
    public float health;
	
	[JsonProperty("hack_timer")]
public float hack_timer;
public float hack_timer_min = 0f;   // если >0 — нижняя граница
public float hack_timer_max = 0f;   // если >0 — верхняя граница
}

private static Vector3 V3(float[] p) => (p != null && p.Length == 3) ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;

// ВСЁ ОСТАЛЬНОЕ В ЭТОМ РЕГИОНЕ ОСТАЁТСЯ БЕЗ ИЗМЕНЕНИЙ
// (CreateDefaultLayouts, LoadLayouts, GetLayout и т.д. - копируй как есть)

        private void CreateDefaultLayouts()
        {
            var dir = Path.Combine(Interface.Oxide.DataDirectory, LayoutDir);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var defaults = new Dictionary<string, TrainLayout>
            {
                ["bandit_full"] = new TrainLayout 
                { 
                    name = "bandit_full", 
                    faction = "BANDIT", 
                    cars = new List<CarSpec> 
                    { 
                        new CarSpec { variant = "LOCO" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" }
                    } 
                },
                ["pmc_full"] = new TrainLayout 
                { 
                    name = "pmc_full", 
                    faction = "PMC", 
                    cars = new List<CarSpec> 
                    { 
                        new CarSpec { variant = "LOCO" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" }
                    } 
                },
                ["coblab_full"] = new TrainLayout 
                { 
                    name = "coblab_full", 
                    faction = "COBLAB", 
                    cars = new List<CarSpec> 
                    { 
                        new CarSpec { variant = "LOCO" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" },
                        new CarSpec { variant = "C" }
                    } 
                },
                
                ["wagonC_samsite"] = new TrainLayout { name = "wagonC_samsite", faction = "COBLAB", cars = new List<CarSpec> { new CarSpec { variant = "C" } } },
                ["wagonC_labcob"] = new TrainLayout { name = "wagonC_labcob", faction = "COBLAB", cars = new List<CarSpec> { new CarSpec { variant = "C" } } },
                ["wagonC_bradley"] = new TrainLayout { name = "wagonC_bradley", faction = "PMC", cars = new List<CarSpec> { new CarSpec { variant = "C" } } },
                ["wagonC_pmc"] = new TrainLayout { name = "wagonC_pmc", faction = "PMC", cars = new List<CarSpec> { new CarSpec { variant = "C" } } },
                ["wagonC_bandit"] = new TrainLayout { name = "wagonC_bandit", faction = "BANDIT", cars = new List<CarSpec> { new CarSpec { variant = "C" } } },
                
                ["loco_coblab"] = new TrainLayout { name = "loco_coblab", faction = "COBLAB", cars = new List<CarSpec> { new CarSpec { variant = "LOCO" } } },
                ["loco_bandit"] = new TrainLayout { name = "loco_bandit", faction = "BANDIT", cars = new List<CarSpec> { new CarSpec { variant = "LOCO" } } },
                ["loco_pmc"] = new TrainLayout { name = "loco_pmc", faction = "PMC", cars = new List<CarSpec> { new CarSpec { variant = "LOCO" } } },

                ["wagonA_bandit"] = new TrainLayout { name = "wagonA_bandit", faction = "BANDIT", cars = new List<CarSpec> { new CarSpec { variant = "A" } } },
                ["wagonA_labcob"] = new TrainLayout { name = "wagonA_labcob", faction = "COBLAB", cars = new List<CarSpec> { new CarSpec { variant = "A" } } },
                ["wagonA_pmc"] = new TrainLayout { name = "wagonA_pmc", faction = "PMC", cars = new List<CarSpec> { new CarSpec { variant = "A" } } },

                ["wagonB_bandit"] = new TrainLayout { name = "wagonB_bandit", faction = "BANDIT", cars = new List<CarSpec> { new CarSpec { variant = "B" } } },
                ["wagonB_labcob"] = new TrainLayout { name = "wagonB_labcob", faction = "COBLAB", cars = new List<CarSpec> { new CarSpec { variant = "B" } } },
                ["wagonB_pmc"] = new TrainLayout { name = "wagonB_pmc", faction = "PMC", cars = new List<CarSpec> { new CarSpec { variant = "B" } } }
            };
            
            foreach (var kv in defaults)
            {
                string filePath = Path.Combine(dir, $"{kv.Key}.json");
                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, JsonConvert.SerializeObject(kv.Value, Formatting.Indented));
            }
        }

private void LoadLayouts()
{
    _layouts.Clear();
    
    // ✅ Принудительная сборка мусора
    System.GC.Collect();
    System.GC.WaitForPendingFinalizers();
    
    var dir = Path.Combine(Interface.Oxide.DataDirectory, LayoutDir);
    if (!Directory.Exists(dir))
        Directory.CreateDirectory(dir);

    Puts($"📂 Загружаем layouts из: {dir}");

    foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
    {
        try
        {
            var json = File.ReadAllText(file, System.Text.Encoding.UTF8);
            
            // ✅ КРИТИЧНО: Логируем размер файла!
            Puts($"📄 Файл: {Path.GetFileName(file)} ({json.Length} байт)");
            
            var settings = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                DefaultValueHandling = DefaultValueHandling.Populate,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            var layout = JsonConvert.DeserializeObject<TrainLayout>(json, settings);
            
            if (layout == null)
            {
                PrintWarning($"⚠️ Layout NULL после десериализации: {Path.GetFileName(file)}");
                continue;
            }
            
            if (string.IsNullOrEmpty(layout.name))
            {
                PrintWarning($"⚠️ Layout.name пусто: {Path.GetFileName(file)}");
                continue;
            }
            
            // ✅ КРИТИЧНО: Проверяем objects!
            int objCount = layout.objects?.Count ?? 0;
            Puts($"   📦 {layout.name}: {objCount} objects (null={layout.objects == null})");
            
            _layouts[layout.name] = layout;
        }
        catch (System.Exception e)
        {
            PrintError($"❌ Ошибка загрузки {Path.GetFileName(file)}: {e.Message}");
        }
    }

    Puts($"✅ Всего загружено layouts: {_layouts.Count}");
}

public void ReloadSingleLayout(string layoutName, string filePath)
{
    try
    {
        Puts($"🔄 Перезагружаю ТОЛЬКО layout: {layoutName}");
        
        var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        
        var settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DefaultValueHandling = DefaultValueHandling.Populate,
            NullValueHandling = NullValueHandling.Ignore
        };
        
        var layout = JsonConvert.DeserializeObject<TrainLayout>(json, settings);
        
        if (layout == null || string.IsNullOrEmpty(layout.name))
        {
            PrintWarning($"❌ Не удалось загрузить layout: {layoutName}");
            return;
        }
        
        // ✅ Обновляем ТОЛЬКО этот layout в кеше!
        _layouts[layout.name] = layout;
        
        Puts($"✅ Layout '{layout.name}' обновлён в кеше ({layout.objects?.Count ?? 0} объектов)");
    }
    catch (System.Exception e)
    {
        PrintError($"❌ Ошибка перезагрузки layout '{layoutName}': {e.Message}");
    }
}

        private TrainLayout GetLayout(string name)
        {
            TrainLayout l;
            return _layouts.TryGetValue(name, out l) ? l : null;
        }

        private TrainLayout ChooseFactionLayout(string faction)
        {
            if (_layouts.Count == 0) return null;
            foreach (var kv in _layouts)
                if (!string.IsNullOrEmpty(kv.Value.faction) && kv.Value.faction.Equals(faction, System.StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            
            using (var en = _layouts.Values.GetEnumerator())
                return en.MoveNext() ? en.Current : null;
        }

        private string GetWagonPrefabByVariant(string variant)
        {
            switch (variant?.ToUpper())
            {
                case "LOCO": return EnginePrefab;
                case "A": return WagonPrefabA;
                case "B": return WagonPrefabB;
                case "C": return WagonPrefabC;
                case "LOOT": return WagonPrefabLoot;
                case "EMPTY": return WagonPrefabUnloaded;
                default: return WagonPrefabC;
            }
        }

        private TrainLayout ResolveLayoutArg(string[] args)
        {
            if (args != null && args.Length > 1 && !string.IsNullOrEmpty(args[1]))
            {
                var byName = GetLayout(args[1]);
                if (byName != null) return byName;
            }
            if (args != null && args.Length > 0)
            {
                var faction = args[0];
                var byFaction = ChooseFactionLayout(faction);
                if (byFaction != null) return byFaction;
            }
            
            using (var en = _layouts.Values.GetEnumerator())
                return en.MoveNext() ? en.Current : null;
        }
        #endregion

        #region HT.TRAIN.ASSEMBLY
        private const float CAR_SPACING = 8f;
        private readonly List<BaseEntity> _spawnedCars = new List<BaseEntity>();
private readonly List<AutoTurret> _spawnedTurrets = new List<AutoTurret>();
private readonly List<SamSite> _spawnedSamSites = new List<SamSite>();
private readonly List<ScientistNPC> _spawnedNPCs = new List<ScientistNPC>();
private Timer _lifecycleTimer = null;
 // Окно реального урона и кэш исходной защиты
 private bool _explosionDamageArmed = false;
 private readonly Dictionary<uint, ProtectionProperties> _savedProtection = new Dictionary<uint, ProtectionProperties>();


        private BaseEntity SpawnCar(string prefab, TrainTrackSpline track, float distOnSpline)
        {
            Vector3 position = track.GetPosition(distOnSpline);
            Vector3 forward = track.GetTangentCubicHermiteWorld(distOnSpline);
            
            if (forward.sqrMagnitude < 0.001f) 
                forward = Vector3.forward;
            
            Quaternion rotation = Quaternion.LookRotation(forward);
            
            TrainCar trainCar = GameManager.server.CreateEntity(prefab, position, rotation) as TrainCar;
            if (trainCar == null)
            {
                PrintError($"❌ CreateEntity вернул null для {prefab}");
                return null;
            }
            
            trainCar.enableSaving = false;
            
            if (trainCar is TrainEngine engine)
            {
                engine.engineForce = 250000f;
                engine.maxSpeed = 18f;
			 engine.OwnerID = HELL_OWNER_ID;
            }
            
            trainCar.Spawn();
            
            if (!trainCar || trainCar.IsDestroyed)
            {
                PrintError($"❌ TrainCar destroyed после Spawn!");
                return null;
            }
            
            trainCar.CancelInvoke(trainCar.DecayTick);
            
            if (trainCar is TrainEngine eng)
            {
                eng.SetFlag(BaseEntity.Flags.On, false);
                eng.SetThrottle(TrainEngine.EngineSpeeds.Zero);
            }
            
            if (trainCar.FrontTrackSection != null)
            {
               // Puts($"   🔧 Выровнен на {trainCar.FrontTrackSection.name} @ {trainCar.FrontWheelSplineDist:F1}м");
            }
            
            NextTick(() =>
            {
                if (trainCar == null || trainCar.IsDestroyed) return;
                
                if (trainCar.platformParentTrigger != null)
                    trainCar.platformParentTrigger.ParentNPCPlayers = true;
            });
            _spawnedTrainEntities.Add(trainCar);
            _spawnedCars.Add(trainCar);
            return trainCar;
        }

        private BaseEntity SpawnCar(string prefab, Vector3 pos, Quaternion rot)
        {
            TrainCar trainCar = GameManager.server.CreateEntity(prefab, pos, rot) as TrainCar;
            if (trainCar == null)
            {
                PrintError($"❌ CreateEntity вернул null для {prefab}");
                return null;
            }
            
            trainCar.enableSaving = false;
            
            if (trainCar is TrainEngine engine)
            {
                engine.engineForce = 250000f;
                engine.maxSpeed = 18f;
				engine.OwnerID = HELL_OWNER_ID;
            }
            
            trainCar.Spawn();
            
            if (!trainCar || trainCar.IsDestroyed)
            {
                PrintError($"❌ TrainCar destroyed после Spawn!");
                return null;
            }
            
            trainCar.CancelInvoke(trainCar.DecayTick);
            
            if (trainCar is TrainEngine eng)
            {
                eng.SetFlag(BaseEntity.Flags.On, false);
                eng.SetThrottle(TrainEngine.EngineSpeeds.Zero);
            }
            
            NextTick(() =>
            {
                if (trainCar == null || trainCar.IsDestroyed) return;
                
                if (trainCar.platformParentTrigger != null)
                    trainCar.platformParentTrigger.ParentNPCPlayers = true;
            });
            _spawnedTrainEntities.Add(trainCar);
            _spawnedCars.Add(trainCar);
            return trainCar;
        }

        private TrainEngine SpawnTrainFromComposition(
            string compositionName, 
            TrainTrackSpline targetTrack,
            float targetDist
        )
        {
            if (!config.Compositions.ContainsKey(compositionName))
            {
                PrintError($"❌ Композиция '{compositionName}' не найдена!");
                return null;
            }

            var comp = config.Compositions[compositionName];
          //  Puts($"🔧 Собираем: {compositionName}, вагонов: {comp.Wagons.Count}");

            ServerMgr.Instance.StartCoroutine(BuildTrainWithSpline(compositionName, comp, targetTrack, targetDist));
            
            return null;
        }

        private IEnumerator BuildTrainWithSpline(
    string compositionName,
    ConfigData.TrainComposition comp, 
    TrainTrackSpline track,
    float splineDist
)
{
    // ✅ ОЧИСТКА СТАРЫХ ВАГОНОВ ПЕРЕД СБОРКОЙ НОВОГО ПОЕЗДА!
    foreach (var entity in _spawnedCars)
    {
        if (entity != null && !entity.IsDestroyed)
            entity.Kill(BaseNetworkable.DestroyMode.None);
    }
    _spawnedCars.Clear();
	_spawnedTrainEntities.Clear();

    
  //  Puts($"🔧 Собираем композицию: {comp.Wagons.Count} вагонов...");
            
    const float SPACING_DISTANCE = 20f;
    
    string firstWagonName = comp.Wagons.Count > 0 ? comp.Wagons[0] : null;
    var firstLayout = !string.IsNullOrEmpty(firstWagonName) ? GetLayout(firstWagonName) : null;
    
    bool firstIsLoco = false;
    if (firstLayout != null && firstLayout.cars != null && firstLayout.cars.Count > 0)
    {
        var firstCar = firstLayout.cars[0];
        firstIsLoco = (firstCar.type?.ToLower() == "locomotive" || firstCar.variant == "LOCO");
    }
    
    int wagonStartIndex = firstIsLoco ? 1 : 0;
    
    List<SpawnPosition> spawnPositions = new List<SpawnPosition>();
    
    TrainTrackSpline currentTrack = track;
    Vector3 currentPosition = currentTrack.GetPosition(splineDist);
    Vector3 currentForward = currentTrack.GetTangentCubicHermiteWorld(splineDist);
    
    spawnPositions.Add(new SpawnPosition(currentPosition, currentForward));
    
    for (int i = wagonStartIndex; i < comp.Wagons.Count; i++)
    {
        TrainTrackSpline.MoveResult result = currentTrack.MoveAlongSpline(
            splineDist, 
            currentForward, 
            SPACING_DISTANCE
        );
        
        currentTrack = result.spline;
        splineDist = result.distAlongSpline;
        currentPosition = currentTrack.GetPosition(splineDist);
        currentForward = currentTrack.GetTangentCubicHermiteWorld(splineDist);
        
        spawnPositions.Add(new SpawnPosition(currentPosition, currentForward));
    }
    
    //Puts($"✅ Рассчитано {spawnPositions.Count} позиций");

    string locoPrefab = EnginePrefab;

    if (firstIsLoco && firstLayout != null && firstLayout.cars != null && firstLayout.cars.Count > 0)
    {
        locoPrefab = GetWagonPrefabByVariant(firstLayout.cars[0].variant);
        Puts($"🚂 Используем локомотив из лэйаута: {firstWagonName}");
    }

    TrainCar locoEnt = GameManager.server.CreateEntity(
        locoPrefab, 
        spawnPositions[0].Position, 
        spawnPositions[0].Rotation
    ) as TrainCar;
    
    locoEnt.enableSaving = false;
    
    if (locoEnt is TrainEngine engine)
    {
        engine.engineForce = 250000f;
        engine.maxSpeed = 18f;
		engine.OwnerID = HELL_OWNER_ID;
    }
    
    locoEnt.frontCoupling = null;
    locoEnt.Spawn();
    locoEnt.OwnerID = HELL_OWNER_ID;
locoEnt.SendNetworkUpdate();
	
    NextTick(() =>
    {
        if (locoEnt != null && !locoEnt.IsDestroyed && locoEnt.platformParentTrigger != null)
            locoEnt.platformParentTrigger.ParentNPCPlayers = true;
    });
    
    locoEnt.CancelInvoke(locoEnt.DecayTick);
    
    TrainEngine trainEngine = locoEnt as TrainEngine;
    TrainCar lastSpawnedCar = locoEnt;

  //  Puts($"🚂 Локомотив создан, ID: {locoEnt.net.ID}");

    _spawnedCars.Add(locoEnt);
	_spawnedTrainEntities.Add(locoEnt);


    yield return new WaitForSeconds(0.5f);
    
    int positionIndex = 1;

    for (int i = wagonStartIndex; i < comp.Wagons.Count; i++)
    {
        string wagonName = comp.Wagons[i];
        var layout = GetLayout(wagonName);
        string prefab = WagonPrefabC;
        
        if (layout != null && layout.cars != null && layout.cars.Count > 0)
        {
            var car = layout.cars[0];
            
            if (car.type?.ToLower() == "locomotive" || car.variant == "LOCO")
            {
                Puts($"⚠️ Вагон [{i}] '{wagonName}' - локомотив, пропускаем");
                continue;
            }
            
            prefab = GetWagonPrefabByVariant(car.variant);
        }
        
        if (positionIndex >= spawnPositions.Count)
        {
            PrintError($"❌ Кончились позиции! Вагон [{i}] не будет создан");
            break;
        }
        
        TrainCar trainCar = GameManager.server.CreateEntity(
            prefab, 
            spawnPositions[positionIndex].Position, 
            spawnPositions[positionIndex].Rotation
        ) as TrainCar;
        
        if (trainCar == null)
        {
            PrintError($"❌ Не удалось создать вагон [{i}]");
            continue;
        }
        
        trainCar.enableSaving = false;
        trainCar.Spawn();
        
        NextTick(() =>
        {
            if (trainCar != null && !trainCar.IsDestroyed && trainCar.platformParentTrigger != null)
                trainCar.platformParentTrigger.ParentNPCPlayers = true;
        });
        
        if (trainCar.IsDestroyed)
        {
            PrintError($"❌ Вагон [{i}] destroyed после Spawn");
            continue;
        }
        
        trainCar.CancelInvoke(trainCar.DecayTick);
        
      //  Puts($"   🔧 [{i}] {wagonName}: {trainCar.ShortPrefabName} (ID: {trainCar.net.ID})");
        _spawnedTrainEntities.Add(trainCar);
        _spawnedCars.Add(trainCar);
        
        yield return new WaitForSeconds(0.2f);
        
        if (trainCar.FrontTrackSection == null)
        {
            PrintError($"   ❌ [{i}] Вагон НЕ привязан к рельсам!");
            lastSpawnedCar = trainCar;
            positionIndex++;
            continue;
        }
        
        if (lastSpawnedCar.rearCoupling == null)
        {
            PrintError($"   ❌ [{i}] У предыдущего вагона нет rearCoupling!");
            lastSpawnedCar = trainCar;
            positionIndex++;
            continue;
        }
        
        if (trainCar.frontCoupling == null)
        {
            PrintError($"   ❌ [{i}] У текущего вагона нет frontCoupling!");
            lastSpawnedCar = trainCar;
            positionIndex++;
            continue;
        }
        
        float distToMove = Vector3Ex.Distance2D(
            lastSpawnedCar.rearCoupling.position, 
            trainCar.frontCoupling.position
        );
        
       // Puts($"   📏 [{i}] Расстояние между сцепками: {distToMove:F2}м");
        
        trainCar.MoveFrontWheelsAlongTrackSpline(
            trainCar.FrontTrackSection, 
            trainCar.FrontWheelSplineDist, 
            distToMove,
            null, 
            0
        );
        
        yield return new WaitForSeconds(0.2f);
        
        bool coupled = trainCar.coupling.frontCoupling.TryCouple(
            lastSpawnedCar.coupling.rearCoupling, 
            true
        );
        
       // Puts($"   {(coupled ? "✅" : "❌")} Сцепка: {lastSpawnedCar.ShortPrefabName} ↔ {trainCar.ShortPrefabName}");
        
        lastSpawnedCar = trainCar;
        positionIndex++;
    }
    
    if (lastSpawnedCar != null && lastSpawnedCar != locoEnt && lastSpawnedCar.rearCoupling != null)
    {
        lastSpawnedCar.rearCoupling = null;
       // Puts($"   🔒 Задняя сцепка отключена для последнего вагона");
    }
    
    yield return new WaitForSeconds(1f);
    
    switch (comp.Tier)
    {
        case ConfigData.TrainTier.LIGHT:
            trainEngine.maxSpeed = config.Speed.TierLight;
            break;
        case ConfigData.TrainTier.MEDIUM:
            trainEngine.maxSpeed = config.Speed.TierMedium;
            break;
        case ConfigData.TrainTier.HEAVY:
            trainEngine.maxSpeed = config.Speed.TierHeavy;
            break;
    }
    trainEngine.engineForce = 250000f;
    
    EntityFuelSystem fuelSystem = trainEngine.GetFuelSystem() as EntityFuelSystem;
    if (fuelSystem != null)
    {
        fuelSystem.AddFuel(500);
        fuelSystem.GetFuelContainer()?.SetFlag(BaseEntity.Flags.Locked, true);
    }
    
    activeHellTrain = trainEngine;
    
    var antiStuckComponent = trainEngine.gameObject.AddComponent<HellTrainComponent>();
    antiStuckComponent.plugin = this;
    antiStuckComponent.engine = trainEngine;
    
    // ✅ ВАЖНО: Спавним объекты С ЗАДЕРЖКОЙ после полной сборки поезда!
    yield return new WaitForSeconds(2f);
    
    // Спавним объекты на локомотив
    if (firstIsLoco && firstLayout != null)
    {
        SpawnLayoutObjects(locoEnt, firstLayout);
        Puts($"   🎯 Объекты локомотива заспавнены из лэйаута: {firstWagonName}");
    }
    
    // Спавним объекты на вагоны
    positionIndex = 1;
    for (int i = wagonStartIndex; i < comp.Wagons.Count; i++)
    {
        if (positionIndex >= _spawnedCars.Count)
            break;
        
        string wagonName = comp.Wagons[i];
        var wagonLayout = GetLayout(wagonName);
        
        if (wagonLayout != null)
        {
            TrainCar wagonCar = _spawnedCars[positionIndex] as TrainCar;
            if (wagonCar != null && !wagonCar.IsDestroyed)
            {
                SpawnLayoutObjects(wagonCar, wagonLayout);
              //  Puts($"   🎯 Объекты вагона [{i}] заспавнены из лэйаута: {wagonName}");
            }
        }
        
        positionIndex++;
        yield return new WaitForSeconds(0.1f);
    }
    
    yield return new WaitForSeconds(0.5f);
    StartEngine(trainEngine);

// ✅ ИНИЦИАЛИЗАЦИЯ LIFECYCLE
_trainLifecycle = new TrainLifecycle(
    compositionName,
    trainEngine.transform.position,
    this
);

string trainName = config.CompositionNames[_trainLifecycle.CompositionType];

// ✅ ИЗМЕНЕНО: АНОНС СПАВНА ИЗ КОНФИГА
string spawnMessage = config.Messages.TrainSpawned
    .Replace("{trainName}", trainName)
    .Replace("{grid}", _trainLifecycle.LastGrid);

Server.Broadcast(spawnMessage);

StopGridCheckTimer();
_gridCheckTimer = timer.Repeat(10f, 0, CheckTrainGrid);

StartLifecycleTimer();
StartEngineWatchdog();

Puts($"✅ Hell Train готов! Вагонов: {comp.Wagons.Count - wagonStartIndex}");
}

        private struct SpawnPosition
        {
            public Vector3 Position;
            public Vector3 Forward;

            public Quaternion Rotation => Forward.magnitude == 0f 
                ? Quaternion.identity * Quaternion.Euler(0f, 180f, 0f) 
                : Quaternion.LookRotation(Forward) * Quaternion.Euler(0f, 180f, 0f);

            public SpawnPosition(Vector3 position, Vector3 forward)
            {
                this.Position = position;
                this.Forward = forward;
            }
        }

        private TrainEngine SpawnTrainFromLayout(TrainLayout layout, Vector3 origin, Quaternion facing)
        {
          //  Puts($"🔧 [SpawnLayout] Layout: {layout.name}, Cars: {layout.cars?.Count ?? 0}");
            
            Vector3 fwd = facing * Vector3.forward;
            BaseEntity last = null;
            TrainEngine engine = null;
            float offset = 0f;

            if (layout.cars == null || layout.cars.Count == 0)
            {
                PrintWarning("⚠️ Layout has no cars!");
                return null;
            }

            foreach (var car in layout.cars)
{
    string prefab = null;
    
    if (car.type?.ToLower() == "locomotive" || car.variant == "LOCO")
        prefab = EnginePrefab;
    else
        prefab = GetWagonPrefabByVariant(car.variant);
    
    Vector3 pos = origin - fwd * offset;
    var carEnt = SpawnCar(prefab, pos, facing);
    
    if (carEnt == null)
    {
        PrintWarning($"⚠️ Spawn failed: {car.type ?? car.variant}");
        continue;
    }

    if (engine == null && carEnt is TrainEngine)
        engine = carEnt as TrainEngine;

    if (last != null)
        CoupleCars(last, carEnt);

    // ✅ СПАВНИМ ОБЪЕКТЫ НА ВАГОНЕ!
    TrainCar trainCar = carEnt as TrainCar;
    if (trainCar != null)
    {
        SpawnLayoutObjects(trainCar, layout);
    }

    last = carEnt;
    offset += CAR_SPACING;
}

            if (engine == null)
            {
                PrintError("❌ No locomotive in layout!");
                return null;
            }

          //  Puts($"✅ Train assembled! Cars: {layout.cars.Count}");
            return engine;
        }
        #endregion

       #region HT.SPAWN.TRAIN

// ✅ НОВОЕ: WEIGHTED RANDOM ВЫБОР КОМПОЗИЦИИ
private string ChooseWeightedComposition()
{
    int totalWeight = config.Compositions.Values.Sum(c => c.Weight);
    
    if (totalWeight <= 0)
    {
        PrintWarning("⚠️ Суммарный вес композиций = 0! Выбираю первую.");
        return config.Compositions.Keys.First();
    }
    
    int random = _rng.Next(0, totalWeight);
    
    foreach (var kv in config.Compositions)
    {
        random -= kv.Value.Weight;
        if (random < 0)
        {
       //    Puts($"🎲 Выбрана композиция: {kv.Key} (вес: {kv.Value.Weight}/{totalWeight})");
            return kv.Key;
        }
    }
    
    return config.Compositions.Keys.First();
}

private void SpawnHellTrain(BasePlayer player = null)
{
	// reset crate state (антиспам + первый ящик)
    if (config.Compositions.Count == 0)
    {
        PrintError("❌ Нет композиций в конфиге!");
        return;
    }

    // ✅ ИЗМЕНЕНО: ИСПОЛЬЗУЕМ WEIGHTED RANDOM
    string chosen = ChooseWeightedComposition();

    if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
    {
        activeHellTrain.Kill();
        activeHellTrain = null;
    }

    int overworldCount = availableOverworldSplines.Count;
    int underworldCount = availableUnderworldSplines.Count;
    
    if (overworldCount == 0 && underworldCount == 0)
    {
        PrintError("❌ Нет доступных треков! Проверь AllowAboveGround/AllowUnderGround в конфиге.");
        return;
    }
    
    bool useUnderground = underworldCount > 0 && (overworldCount == 0 || UnityEngine.Random.value > 0.5f);
    
    List<TrainTrackSpline> tracksToUse = useUnderground ? availableUnderworldSplines : availableOverworldSplines;
    
    int maxAttempts = Mathf.Min(10, tracksToUse.Count);
    
    for (int attempt = 0; attempt < maxAttempts; attempt++)
    {
        TrainTrackSpline trackSpline = tracksToUse[UnityEngine.Random.Range(0, tracksToUse.Count)];
        float length = trackSpline.GetLength();
        
        if (length < config.MinTrackLength)
        {
          //  Puts($"⚠️ Попытка {attempt + 1}/{maxAttempts}: трек {trackSpline.name} слишком короткий ({length:F0}м)");
            continue;
        }
        
        float start = length * 0.15f;
        float end = length * 0.85f;
        float distOnSpline = UnityEngine.Random.Range(start, end);
        
      //  Puts($"🎲 Попытка {attempt + 1}: {(useUnderground ? "подземный" : "наземный")} трек: {trackSpline.name}");
      //  Puts($"🎲 Длина трека: {length:F0}м, позиция: {distOnSpline:F1}м");

        SpawnTrainFromComposition(chosen, trackSpline, distOnSpline);
        
        Puts($"✅ Запущена сборка Hell Train: {chosen}");
        return;
    }
    
    PrintError($"❌ Не удалось найти подходящий трек за {maxAttempts} попыток!");
    
    if (config.AutoRespawn)
    {
        timer.Once(10f, () => SpawnHellTrain());
        Puts("🔄 Попробую снова через 10 секунд...");
    }
}



#endregion

private void ForceDestroyHellTrainHard()
{
    try
    {
        // 1) Снести всё, что мы трекали при спавне
        foreach (var e in _spawnedTrainEntities.ToArray())
	            KillEntitySafe(e);
        _spawnedTrainEntities.Clear();

        // 2) Снести все TrainCar, что остались в мире (и их детей)
        var trainCars = BaseNetworkable.serverEntities.OfType<TrainCar>().ToArray();
        foreach (var car in trainCars)
            KillEntitySafe(car);

        // ❌ 3) Больше НЕ подметаем Vis.Entities по радиусу — чтобы не задевать игроков

        // 4) Сброс внутреннего состояния
        _trainLifecycle = null;
       
    }
    catch (Exception ex)
    {
        PrintError($"ForceDestroyHellTrainHard ERR: {ex}");
    }
}


private void KillEntitySafe(BaseNetworkable e)
{
    if (e == null || e.IsDestroyed) return;

    // 🚫 Никогда не трогаем живых игроков
    if (e is BasePlayer) return;

    var be = e as BaseEntity;
    try
    {
        if (be != null)
        {
            be.CancelInvoke();
            be.SetParent(null, true, true);

            if (be is TrainCar tc)
            {
                var eng = tc as TrainEngine;
                if (eng != null)
                {
                    try { eng.SetFlag(BaseEntity.Flags.On, false, false, true); } catch { }
                    try { eng.SetThrottle(TrainEngine.EngineSpeeds.Zero); } catch { }
                }
                else
                {
                    try { tc.SetFlag(BaseEntity.Flags.On, false); } catch { }
                }

                // Убиваем только дочерние сущности вагона (NPC/турели/прочее), НО не игроков
                var children = tc.children?.ToArray() ?? Array.Empty<BaseEntity>();
                foreach (var child in children)
                {
                    if (child == null || child.IsDestroyed) continue;
                    if (child is BasePlayer) continue;

                    var npcChild = child as NPCPlayer;
                    if (npcChild != null)
                    {
                        try { npcChild.inventory?.Strip(); } catch { }
                    }
                    try { child.Kill(BaseNetworkable.DestroyMode.None); } catch { }
                }
            }

            // Если это сам NPC — зачистить инвентарь
            var np = be as NPCPlayer;
            if (np != null)
            {
                try { np.inventory?.Strip(); } catch { }
            }
        }
    }
    catch { /* ignore */ }

    try { e.Kill(BaseNetworkable.DestroyMode.None); } catch { /* ignore */ }
}





        #region HT.ENGINE.CONTROL
        private void StartEngine(TrainEngine engine)
        {
            if (!engine || engine.IsDestroyed) return;
            
            Puts($"🔧 Запускаем двигатель ID: {engine.net.ID}");
            
            engine.SetFlag(BaseEntity.Flags.On, true, false, true);
            
            if (engine.engineController != null)
                engine.SetFlag(engine.engineController.engineStartingFlag, false, false, true);
            
            engine.SetThrottle(TrainEngine.EngineSpeeds.Fwd_Hi);
            Puts("🚂 Локомотив едет вперёд!");
            
            engine.InvokeRandomized(() => EnsureEngineRunning(engine), 1f, 1f, 0.1f);
            engine.InvokeRandomized(() => CheckRefreshFuel(engine), 5f, 5f, 0.5f);
            
            Puts($"✅ Двигатель запущен!");
        }

        private void ReCoupleAllCars(TrainEngine engine)
        {
            if (engine == null || engine.IsDestroyed) return;
            
            var completeTrain = engine.completeTrain;
            if (completeTrain == null || completeTrain.trainCars == null) 
            {
                Puts("⚠️ completeTrain == null, пробуем найти вагоны вручную");
                
                var nearCars = new List<TrainCar>();
                foreach (var e in _spawnedCars)
                {
                    if (e != null && !e.IsDestroyed && e is TrainCar)
                        nearCars.Add(e as TrainCar);
                }
                
                if (nearCars.Count <= 1)
                {
                    PrintWarning("⚠️ Недостаточно вагонов для сцепки");
                    return;
                }
                
                nearCars = nearCars.OrderBy(c => Vector3.Distance(engine.transform.position, c.transform.position)).ToList();
                
            //    Puts($"🔗 Пересцепка {nearCars.Count} вагонов вручную...");
                
                for (int i = 0; i < nearCars.Count - 1; i++)
                {
                    var front = nearCars[i];
                    var rear = nearCars[i + 1];
                    
                    if (front == null || rear == null) continue;
                    
                    front.coupling.rearCoupling.TryCouple(rear.coupling.frontCoupling, true);
                    Puts($"   ↔ {front.ShortPrefabName} → {rear.ShortPrefabName}");
                }
                
                return;
            }
            
          //  Puts($"🔗 Пересцепка... вагонов в completeTrain: {completeTrain.trainCars.Count}");
            
            for (int i = 0; i < completeTrain.trainCars.Count - 1; i++)
            {
                var front = completeTrain.trainCars[i];
                var rear = completeTrain.trainCars[i + 1];
                
                if (front == null || rear == null) continue;
                
                front.coupling.rearCoupling.TryCouple(rear.coupling.frontCoupling, true);
            }
        }

        private void EnsureEngineRunning(TrainEngine engine)
        {
            if (engine == null || engine.IsDestroyed) return;
            
            if (!engine.HasFlag(BaseEntity.Flags.On))
            {
                engine.SetFlag(BaseEntity.Flags.On, true, false, true);
                engine.SetThrottle(TrainEngine.EngineSpeeds.Fwd_Hi);
            }
        }

        private void CheckRefreshFuel(TrainEngine engine)
        {
            if (engine == null || engine.IsDestroyed) return;
            
            EntityFuelSystem fuel = engine.GetFuelSystem() as EntityFuelSystem;
            if (fuel != null && fuel.GetFuelAmount() < 100)
                fuel.AddFuel(500);
        }
        #endregion

        #region HT.CODELOCK
        private string trainCode = "6666";
        private HashSet<ulong> authorizedPlayers = new HashSet<ulong>();

        private void AddCodeLockToTrain(TrainEngine engine)
        {
         //   Puts($"🔒 Поезд защищён виртуальным кодом: {trainCode}");
        }

        private void RemoveCodeLock()
        {
            authorizedPlayers.Clear();
        //   Puts("🔓 Авторизации сброшены");
        }
        #endregion

        #region HT.COUPLE
        private void CoupleCars(BaseEntity front, BaseEntity rear)
        {
            TrainCar frontCar = front as TrainCar;
            TrainCar rearCar = rear as TrainCar;
            
            if (frontCar == null || rearCar == null) 
            {
                PrintWarning("⚠️ CoupleCars: не TrainCar!");
                return;
            }
            
            float dist = Vector3.Distance(front.transform.position, rear.transform.position);
          //  Puts($"      🔗 Расстояние для сцепки: {dist:F1}м");
            
            if (dist > 20f) 
            {
                PrintWarning($"⚠️ Слишком далеко: {dist:F1}м > 20м");
                return;
            }
            
            bool coupled = frontCar.coupling.rearCoupling.TryCouple(rearCar.coupling.frontCoupling, true);
          // Puts($"      {(coupled ? "✅" : "❌")} Сцепка: {frontCar.ShortPrefabName} ↔ {rearCar.ShortPrefabName}");
        }
        #endregion

        #region HT.UTILS
private TrainEngine GetNearestEngine(BasePlayer player, float maxDistance = 50f)
{
    if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
    {
        float dist = Vector3.Distance(player.transform.position, activeHellTrain.transform.position);
        if (dist <= maxDistance)
            return activeHellTrain;
    }
    
    var allTrains = UnityEngine.Object.FindObjectsOfType<TrainEngine>();
    TrainEngine nearest = null;
    float nearestDist = maxDistance;
    
    foreach (var train in allTrains)
    {
        if (train == null || train.IsDestroyed) continue;
        
        float dist = Vector3.Distance(player.transform.position, train.transform.position);
        if (dist < nearestDist)
        {
            nearest = train;
            nearestDist = dist;
        }
    }
    
    return nearest;
}

// ✅ ВЫНЕСЕН КАК ОТДЕЛЬНЫЙ МЕТОД!
private string GetGridPosition(Vector3 position)
{
    float gridSize = TerrainMeta.Size.x / 26f;
    
    int x = Mathf.FloorToInt((position.x + TerrainMeta.Size.x / 2) / gridSize);
    int z = Mathf.FloorToInt((position.z + TerrainMeta.Size.z / 2) / gridSize);
    
    char letter = (char)('A' + Mathf.Clamp(x, 0, 25));
    int number = Mathf.Clamp(z, 0, 25);
    
    return $"{letter}{number}";
}
#endregion

        #region HT.COMMANDS
		
[ChatCommand("ht.wipe_all_cars")]
private void CmdWipeAllCars(BasePlayer player, string cmd, string[] args)
{
    if (player != null && !player.IsAdmin) { SendReply(player, "Недостаточно прав."); return; }

    _suppressHooks = true;
    StopEngineWatchdog();
    StopGridCheckTimer();
    CancelLifecycleTimer();

    int killed = 0;
    try
    {
        // снимок всех TrainCar
        var snapshot = Pool.GetList<TrainCar>();
        foreach (var bn in BaseNetworkable.serverEntities)
        {
            var car = bn as TrainCar;
            if (car != null && !car.IsDestroyed) snapshot.Add(car);
        }
        foreach (var car in snapshot) { car.Kill(); killed++; }
        Pool.FreeList(ref snapshot);

        // чистим локальные трекеры
        _spawnedCars.Clear();
        _spawnedTrainEntities.Clear();
        _spawnedTurrets.Clear();
        _spawnedSamSites.Clear();
        _spawnedNPCs.Clear();
        _savedProtection.Clear();
        _explosionDamageArmed = false;
        _explodedOnce = false;
        activeHellTrain = null;
        _trainLifecycle = null;

        SendReply(player, $"Helltrain: глобально удалено TrainCar = {killed}");
        Puts($"[Helltrain] wipe_all_cars (chat) → killed={killed}");
    }
    finally
    {
        _suppressHooks = false;
        _engineCleanupTriggered = false;
        _engineCleanupCooldownUntil = 0f;
    }
}

[ConsoleCommand("helltrain.wipe_all_cars")]
private void CcmdWipeAllCars(ConsoleSystem.Arg arg)
{
    _suppressHooks = true;
    StopEngineWatchdog();
    StopGridCheckTimer();
    CancelLifecycleTimer();

    int killed = 0;
    try
    {
        var snapshot = Pool.GetList<TrainCar>();
        foreach (var bn in BaseNetworkable.serverEntities)
        {
            var car = bn as TrainCar;
            if (car != null && !car.IsDestroyed) snapshot.Add(car);
        }
        foreach (var car in snapshot) { car.Kill(); killed++; }
        Pool.FreeList(ref snapshot);

        _spawnedCars.Clear();
        _spawnedTrainEntities.Clear();
        _spawnedTurrets.Clear();
        _spawnedSamSites.Clear();
        _spawnedNPCs.Clear();
        _savedProtection.Clear();
        _explosionDamageArmed = false;
        _explodedOnce = false;
        activeHellTrain = null;
        _trainLifecycle = null;

        arg.ReplyWith($"Helltrain: глобально удалено TrainCar = {killed}");
        Puts($"[Helltrain] wipe_all_cars (console) → killed={killed}");
    }
    finally
    {
        _suppressHooks = false;
        _engineCleanupTriggered = false;
        _engineCleanupCooldownUntil = 0f;
    }
}



		
		[ChatCommand("ht.clean_event_cars")]
private void CmdCleanEventCars(BasePlayer player, string cmd, string[] args)
{
    if (player != null && !player.IsAdmin)
    {
        SendReply(player, "Недостаточно прав.");
        return;
    }
    KillEventTrainCars("manual_command");
    SendReply(player, "Helltrain: ивентовые вагоны очищены.");
}

[ConsoleCommand("helltrain.clean_event_cars")]
private void CcmdCleanEventCars(ConsoleSystem.Arg arg)
{
    KillEventTrainCars("console_command");
    arg.ReplyWith("Helltrain: ивентовые вагоны очищены.");
}

		
		
		[ChatCommand("ht.counts")]
private void CmdCounts(BasePlayer p, string cmd, string[] args)
{
    SendReply(p, $"cars={_spawnedCars.Count}, ents={_spawnedTrainEntities.Count}, turrets={_spawnedTurrets.Count}, sams={_spawnedSamSites.Count}, npcs={_spawnedNPCs.Count}");
}

[ChatCommand("ht.resetflags")]
private void CmdResetFlags(BasePlayer p, string cmd, string[] args)
{
    _explosionTimerArmedOnce = false;
    _explodedOnce = false;
    _explosionDamageArmed = false;
    SendReply(p, "flags reset");
}

		
		[ChatCommand("htdel")]
private void CmdHtDelCrate(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }
    
    // Ищем ящик через raycast
    RaycastHit hit;
    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 10f))
    {
        player.ChatMessage("❌ Смотри на ящик! (макс 10м)");
        return;
    }
    
    BaseEntity entity = hit.GetEntity();
    if (entity == null)
    {
        player.ChatMessage("❌ Не найден объект!");
        return;
    }
    
    HackableLockedCrate crate = entity as HackableLockedCrate;
    if (crate == null)
    {
        player.ChatMessage($"❌ Это не ящик! ({entity.ShortPrefabName})");
        return;
    }
    
    var defender = crate.GetComponent<HellTrainDefender>();
    if (defender == null)
    {
        player.ChatMessage("⚠️ Это не ящик Hell Train!");
        return;
    }
    
    Vector3 pos = crate.transform.position;
    crate.Kill(BaseNetworkable.DestroyMode.None);
    
    player.ChatMessage($"✅ Ящик удалён! Поз: {pos}");
    Puts($"🗑️ {player.displayName} удалил ящик в {pos}");
}
		
		[ChatCommand("htclear")]
private void CmdHtClearCrates(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }
    
    int removed = 0;
    
    // Удаляем все ящики с компонентом HellTrainDefender
    var allCrates = UnityEngine.Object.FindObjectsOfType<HackableLockedCrate>();
    
    foreach (var crate in allCrates)
    {
        if (crate == null || crate.IsDestroyed) continue;
        
        var defender = crate.GetComponent<HellTrainDefender>();
        if (defender != null)
        {
            crate.Kill(BaseNetworkable.DestroyMode.None);
            removed++;
        }
    }
    
    player.ChatMessage($"🧹 Удалено ящиков Hell Train: {removed}");
    Puts($"🧹 {player.displayName} удалил {removed} ящиков Hell Train");
}

/// <summary>
/// Принудительное удаление Hell Train (через команду или автоматически)
/// </summary>
private void ForceDestroyHellTrain()
{
	
KillEventTrainCars("force_destroy");
return;
	RestoreProtectionForAll();
	_spawnedCars.Clear();
_trainLifecycle = null;
    if (activeHellTrain == null || activeHellTrain.IsDestroyed)
    {
        Puts("⚠️ ForceDestroy: Hell Train уже удалён");
        return;
    }

   // Puts($"🧹 Принудительное удаление Hell Train (ID: {activeHellTrain.net.ID})...");

    _allowDestroy = true; // ✅ РАЗРЕШАЕМ УДАЛЕНИЕ

    // Удаляем компонент защиты от застревания
    var antiStuckComponent = activeHellTrain.GetComponent<HellTrainComponent>();
    if (antiStuckComponent != null)
    {
        UnityEngine.Object.Destroy(antiStuckComponent);
        Puts("   ✅ Компонент HellTrainComponent удалён");
    }

    // Останавливаем Invoke
    activeHellTrain.CancelInvoke();

    // Удаляем вагоны
    int count = 0;
    foreach (var entity in _spawnedCars)
    {
        if (entity != null && !entity.IsDestroyed)
        {
            entity.Kill(BaseNetworkable.DestroyMode.None);
            count++;
        }
    }
    _spawnedCars.Clear();
_trainLifecycle = null;
 //   Puts($"   ✅ Удалено вагонов: {count}");

    // ВАЖНО: Обнуляем ПЕРЕД Kill()
    TrainEngine tempEngine = activeHellTrain;
    activeHellTrain = null;

    // Удаляем локомотив
    if (tempEngine != null && !tempEngine.IsDestroyed)
        tempEngine.Kill(BaseNetworkable.DestroyMode.None);

    _allowDestroy = false; // ✅ ЗАПРЕЩАЕМ ОБРАТНО

  //  Puts("✅ Hell Train полностью удалён");
}

[ChatCommand("htinfo")]
private void CmdHtInfo(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }

    var sb = new StringBuilder();
    sb.AppendLine("════════════════════════════════════════");
    sb.AppendLine("📋 HELL TRAIN - КОМАНДЫ");
    sb.AppendLine("════════════════════════════════════════");
    sb.AppendLine("");
    
    // ОСНОВНЫЕ
    sb.AppendLine("🚂 ОСНОВНЫЕ:");
    sb.AppendLine("  /helltrain startnear [composition] - Спавн рядом");
    sb.AppendLine("  /htspawn <name> - Спавн композиции");
    sb.AppendLine("  /htcleanup [hell] - Удалить поезда");
    sb.AppendLine("  /htcheck - Инфо о поезде");
    sb.AppendLine("  /http - ТП к поезду");
    sb.AppendLine("");
    
    // РЕДАКТОР
    sb.AppendLine("✏️ РЕДАКТОР:");
    sb.AppendLine("  /htedit load <layoutName> - Открыть редактор");
    sb.AppendLine("  /htedit save - Сохранить изменения");
    sb.AppendLine("  /htedit cancel - Закрыть без сохранения");
    sb.AppendLine("  /htedit spawn <type> [args] - Создать объект");
    sb.AppendLine("  /htedit move - Переместить (смотри на объект)");
    sb.AppendLine("  /htedit delete - Удалить (смотри на объект)");
    sb.AppendLine("");
    
    // SPAWN ТИПЫ
    sb.AppendLine("📦 ТИПЫ ДЛЯ SPAWN:");
    sb.AppendLine("  npc <kitname> - NPC с китом");
    sb.AppendLine("    Пример: /htedit spawn npc pmcjuggernaut");
    sb.AppendLine("  turret [gun] [ammo] [count] - Турель");
    sb.AppendLine("    Пример: /htedit spawn turret m249 ammo.rifle 500");
    sb.AppendLine("  samsite - SAM турель");
    sb.AppendLine("  loot - Hackable ящик");
    sb.AppendLine("");
    
    // УТИЛИТЫ
    sb.AppendLine("🔧 УТИЛИТЫ:");
    sb.AppendLine("  /htpos - Твоя позиция от поезда");
    sb.AppendLine("  /htreload - Перезагрузить лэйауты");
    sb.AppendLine("");
    sb.AppendLine("🔍 ДИАГНОСТИКА:");
    sb.AppendLine("  /htdebug npc - Маркеры NPC");
    sb.AppendLine("  /htdebug turret - Маркеры турелей");
    sb.AppendLine("  /htdebug samsite - SAM турели");
    sb.AppendLine("  /htdebug loot - Ящики с лутом");
    sb.AppendLine("  /htdebug all - Полная диагностика вагона");
    sb.AppendLine("");
    
    // WAGON УТИЛИТЫ
    sb.AppendLine("🗑️ ОЧИСТКА:");
    sb.AppendLine("  /wagon.remove <type> - Удалить объекты");
    sb.AppendLine("    Типы: npc, turret, samsite, loot, all");
    sb.AppendLine("  /wagon.undo - Отменить последнее");
    sb.AppendLine("  /wagon.list - Список объектов");
    sb.AppendLine("");
    
    // УПРАВЛЕНИЕ
    sb.AppendLine("🎮 УПРАВЛЕНИЕ В РЕДАКТОРЕ:");
    sb.AppendLine("  ЛКМ - разместить объект");
    sb.AppendLine("  ПКМ - отменить размещение");
    sb.AppendLine("  RELOAD - поворот объекта");
    sb.AppendLine("  DUCK+RELOAD - поворот по Z");
    sb.AppendLine("  SPRINT+RELOAD - поворот по X");
    sb.AppendLine("");
    
    // ДОСТУПНЫЕ КОМПОЗИЦИИ
    sb.AppendLine("📋 ДОСТУПНЫЕ КОМПОЗИЦИИ:");
    foreach (var kv in config.Compositions)
    {
        var comp = kv.Value;
        sb.AppendLine($"  • {kv.Key} ({comp.Tier}, {comp.Wagons.Count} вагонов)");
    }
    sb.AppendLine("");
    
    // ЛЭЙАУТЫ
    sb.AppendLine("📦 ЗАГРУЖЕННЫЕ ЛЭЙАУТЫ:");
    int layoutCount = 0;
    foreach (var kv in _layouts)
    {
        if (layoutCount < 10)
        {
            int objCount = kv.Value.objects?.Count ?? 0;
            sb.AppendLine($"  • {kv.Key} ({objCount} объектов)");
        }
        layoutCount++;
    }
    if (layoutCount > 10)
        sb.AppendLine($"  ... и еще {layoutCount - 10}");
    sb.AppendLine("");
    
    sb.AppendLine("════════════════════════════════════════");
    
    player.ChatMessage(sb.ToString());
}

[ChatCommand("htdebug")]
private void CmdHtDebug(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }

    string mode = args.Length > 0 ? args[0].ToLower() : "all";

    // Найти ближайший вагон
    TrainCar nearestCar = null;
    float nearestDist = 20f;

    foreach (var entity in _spawnedCars)
    {
        if (entity == null || entity.IsDestroyed) continue;
        if (!(entity is TrainCar car)) continue;

        float dist = Vector3.Distance(player.transform.position, car.transform.position);
        if (dist < nearestDist)
        {
            nearestCar = car;
            nearestDist = dist;
        }
    }

    if (nearestCar == null)
    {
        player.ChatMessage("❌ Вагон не найден в радиусе 20м");
        return;
    }

    var sb = new StringBuilder();
    sb.AppendLine($"════════════════════════════════════════");
    sb.AppendLine($"🔍 DEBUG: {nearestCar.ShortPrefabName}");
    sb.AppendLine($"Расстояние: {nearestDist:F1}м");
    sb.AppendLine($"════════════════════════════════════════");

    int npcCount = 0;
    int turretCount = 0;
    int samsiteCount = 0;
    int lootCount = 0;
    int otherCount = 0;

    // Собираем все child entities
    var children = new List<BaseEntity>();
    foreach (var child in nearestCar.children)
    {
        if (child == null) continue;
        children.Add(child);
    }

    // Также проверяем спавненные объекты рядом
    foreach (var entity in _spawnedCars)
    {
        if (entity == null || entity.IsDestroyed) continue;
        if (entity == nearestCar) continue;
        if (entity.GetParentEntity() == nearestCar)
            children.Add(entity);
    }

    if (mode == "npc" || mode == "all")
    {
        sb.AppendLine("");
        sb.AppendLine("👤 NPC:");
        foreach (var child in children)
        {
            if (!(child is ScientistNPC npc)) continue;

            var marker = npc.GetComponent<NPCTypeMarker>();
            string npcType = marker?.npcType ?? "❌ НЕТ МАРКЕРА";
            
            Vector3 localPos = nearestCar.transform.InverseTransformPoint(npc.transform.position);
            
            int itemCount = (npc.inventory?.containerMain?.itemList.Count ?? 0)
                          + (npc.inventory?.containerBelt?.itemList.Count ?? 0)
                          + (npc.inventory?.containerWear?.itemList.Count ?? 0);

            string weapon = "нет";
            if (npc.inventory?.containerBelt != null)
            {
                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    var held = item?.GetHeldEntity();
                    if (held != null)
                    {
                        weapon = item.info.shortname;
                        break;
                    }
                }
            }

            sb.AppendLine($"  • Type: {npcType}");
            sb.AppendLine($"    Оружие: {weapon}");
            sb.AppendLine($"    Предметов: {itemCount}");
            sb.AppendLine($"    Локальная поз: {localPos}");
            sb.AppendLine($"    HP: {npc.Health():F0}/{npc.MaxHealth():F0}");
            sb.AppendLine("");
            
            npcCount++;
        }
        if (npcCount == 0)
            sb.AppendLine("  (нет NPC)");
    }

    if (mode == "turret" || mode == "all")
    {
        sb.AppendLine("");
        sb.AppendLine("🔫 ТУРЕЛИ:");
        foreach (var child in children)
        {
            if (!(child is AutoTurret turret)) continue;

            var marker = turret.GetComponent<TurretMarker>();
            string gun = marker?.gun ?? "❌ НЕТ МАРКЕРА";
            string ammo = marker?.ammo ?? "?";
            int ammoCount = marker?.ammoCount ?? 0;

            Vector3 localPos = nearestCar.transform.InverseTransformPoint(turret.transform.position);

            string actualGun = "пусто";
            string actualAmmo = "пусто";
            int actualAmmoCount = 0;

            if (turret.inventory != null)
            {
                if (turret.inventory.itemList.Count > 0)
                    actualGun = turret.inventory.itemList[0]?.info?.shortname ?? "?";
                if (turret.inventory.itemList.Count > 1)
                {
                    actualAmmo = turret.inventory.itemList[1]?.info?.shortname ?? "?";
                    actualAmmoCount = turret.inventory.itemList[1]?.amount ?? 0;
                }
            }

            sb.AppendLine($"  • Маркер: {gun} + {ammo} x{ammoCount}");
            sb.AppendLine($"    Реально: {actualGun} + {actualAmmo} x{actualAmmoCount}");
            sb.AppendLine($"    Локальная поз: {localPos}");
            sb.AppendLine($"    HP: {turret.Health():F0}/{turret.MaxHealth():F0}");
            sb.AppendLine($"    Включена: {turret.IsOn()}");
            sb.AppendLine("");
            
            turretCount++;
        }
        if (turretCount == 0)
            sb.AppendLine("  (нет турелей)");
    }

    if (mode == "samsite" || mode == "all")
    {
        sb.AppendLine("");
        sb.AppendLine("🚀 SAM SITES:");
        foreach (var child in children)
        {
            if (!(child is SamSite sam)) continue;

            Vector3 localPos = nearestCar.transform.InverseTransformPoint(sam.transform.position);

            sb.AppendLine($"  • Локальная поз: {localPos}");
            sb.AppendLine($"    HP: {sam.Health():F0}/{sam.MaxHealth():F0}");
            sb.AppendLine("");
            
            samsiteCount++;
        }
        if (samsiteCount == 0)
            sb.AppendLine("  (нет SAM)");
    }

    if (mode == "loot" || mode == "all")
    {
        sb.AppendLine("");
        sb.AppendLine("📦 ЯЩИКИ:");
        foreach (var child in children)
        {
            if (!(child is HackableLockedCrate crate)) continue;

            Vector3 localPos = nearestCar.transform.InverseTransformPoint(crate.transform.position);

            int itemCount = crate.inventory?.itemList?.Count ?? 0;
            bool hasDefender = crate.GetComponent<HellTrainDefender>() != null;

            sb.AppendLine($"  • Локальная поз: {localPos}");
            sb.AppendLine($"    Предметов: {itemCount}");
            sb.AppendLine($"    HP: {crate.Health():F0}");
            sb.AppendLine($"    Компонент защиты: {(hasDefender ? "✅" : "❌")}");
            sb.AppendLine("");
            
            lootCount++;
        }
        if (lootCount == 0)
            sb.AppendLine("  (нет ящиков)");
    }

    sb.AppendLine("");
    sb.AppendLine($"ВСЕГО: NPC={npcCount}, Турели={turretCount}, SAM={samsiteCount}, Ящики={lootCount}");
    sb.AppendLine($"════════════════════════════════════════");

    player.ChatMessage(sb.ToString());
}

[ChatCommand("htcheck")]
private void CmdCheckTrain(BasePlayer player, string command, string[] args)
{
    if (activeHellTrain == null || activeHellTrain.IsDestroyed)
    {
        player.ChatMessage("❌ Hell Train не активен!");
        return;
    }
    
    player.ChatMessage($"🚂 Hell Train ID: {activeHellTrain.net.ID}");
    player.ChatMessage($"   Позиция: {activeHellTrain.transform.position}");
    
    var completeTrain = activeHellTrain.completeTrain;
    if (completeTrain != null && completeTrain.trainCars != null)
    {
        player.ChatMessage($"   Вагонов в составе: {completeTrain.trainCars.Count}");
        
        foreach (var car in completeTrain.trainCars)
        {
            if (car == null) continue;
            player.ChatMessage($"      - {car.ShortPrefabName} (ID: {car.net.ID})");
        }
    }
    else
    {
        player.ChatMessage("   ⚠️ completeTrain == null!");
    }
    
    player.ChatMessage($"📦 В _spawnedCars: {_spawnedCars.Count}");
    int alive = 0;
    foreach (var e in _spawnedCars)
    {
        if (e != null && !e.IsDestroyed) alive++;
    }
    player.ChatMessage($"   Живых: {alive}");
}

[ChatCommand("http")]
private void CmdTeleportToTrain(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }
    
    if (activeHellTrain == null || activeHellTrain.IsDestroyed)
    {
        player.ChatMessage("❌ Hell Train не активен!");
        return;
    }
    
    Vector3 trainPos = activeHellTrain.transform.position;
    Vector3 trainFwd = activeHellTrain.transform.forward;
    Vector3 tpPos = trainPos + trainFwd * 5f + Vector3.up * 2f;
    
    player.Teleport(tpPos);
    player.ChatMessage($"✅ ТП к Hell Train! Pos: {trainPos}");
    
    int carCount = 0;
    var completeTrain = activeHellTrain.completeTrain;
    if (completeTrain != null)
        carCount = completeTrain.trainCars.Count;
    
    player.ChatMessage($"🚂 Вагонов в составе: {carCount}");
}

[ChatCommand("htcode")]
private void EnterCodeCommand(BasePlayer player, string command, string[] args)
{
    if (args.Length == 0)
    {
        player.ChatMessage("Используй: /htcode 6666");
        return;
    }
    
    if (args[0] == trainCode)
    {
        authorizedPlayers.Add(player.userID);
        player.ChatMessage("✅ Код принят! Можешь сесть.");
    }
    else
    {
        player.ChatMessage("❌ Неверный код!");
    }
}

[ChatCommand("wagon.remove")]
private void CmdWagonRemove(BasePlayer player, string command, string[] args)
{
    WagonEditor editor = player.GetComponent<WagonEditor>();
    if (editor == null)
    {
        player.ChatMessage("Редактор не активен!");
        return;
    }
    
    if (args.Length == 0)
    {
        player.ChatMessage("Использование: /wagon.remove <тип>");
        player.ChatMessage("Типы: bradley, turret, samsite, npc, crate, all");
        return;
    }
    
    string type = args[0].ToLower();
    int removed = 0;
    
    List<BaseEntity> toRemove = new List<BaseEntity>();
    
    foreach (var child in editor.GetChildren())
    {
        bool shouldRemove = false;
        
        switch (type)
        {
            case "turret":
    shouldRemove = child is AutoTurret;
    break;
            case "samsite":
                shouldRemove = child is SamSite;
                break;
            case "npc":
                shouldRemove = child is global::HumanNPC;
                break;
            case "crate":
                shouldRemove = child.ShortPrefabName.Contains("crate");
                break;
            case "all":
                shouldRemove = true;
                break;
        }
        
        if (shouldRemove)
            toRemove.Add(child);
    }
    
    foreach (var entity in toRemove)
    {
        editor.DeleteWagonEntity(entity);
        removed++;
    }
    
    player.ChatMessage($"Удалено объектов: {removed}");
}

[ChatCommand("wagon.undo")]
private void CmdWagonUndo(BasePlayer player)
{
    WagonEditor editor = player.GetComponent<WagonEditor>();
    if (editor == null)
    {
        player.ChatMessage("Редактор не активен!");
        return;
    }
    
    var children = editor.GetChildren();
    if (children.Count == 0)
    {
        player.ChatMessage("Нет объектов для удаления!");
        return;
    }
    
    var last = children[children.Count - 1];
    editor.DeleteWagonEntity(last);
    player.ChatMessage($"Удалён: {last.ShortPrefabName}");
}

[ChatCommand("wagon.list")]
private void CmdWagonList(BasePlayer player)
{
    WagonEditor editor = player.GetComponent<WagonEditor>();
    if (editor == null)
    {
        player.ChatMessage("Редактор не активен!");
        return;
    }
    
    var children = editor.GetChildren();
    if (children.Count == 0)
    {
        player.ChatMessage("Нет объектов!");
        return;
    }
    
    player.ChatMessage($"=== Объекты на вагоне ({children.Count}) ===");
    for (int i = 0; i < children.Count; i++)
    {
        var child = children[i];
        string name = child.ShortPrefabName;
        if (child is BradleyAPC) name = "Bradley APC";
        if (child is AutoTurret) name = "Auto Turret";
        if (child is SamSite) name = "SAM Site";
        if (child is global::HumanNPC) name = "NPC";
        
        player.ChatMessage($"{i + 1}. {name}");
    }
}

[ChatCommand("htspawn")]
private void CmdSpawnComposition(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }
    
    if (args.Length == 0)
    {
        player.ChatMessage("📋 Доступные композиции:");
        foreach (var key in config.Compositions.Keys)
        {
            var comp = config.Compositions[key];
            player.ChatMessage($"   • {key} ({comp.Tier}, {comp.Wagons.Count} вагонов)");
        }
        player.ChatMessage("Используй: /htspawn <название>");
        return;
    }
    
    string compositionName = args[0].ToLower();
    
    if (!config.Compositions.ContainsKey(compositionName))
    {
        player.ChatMessage($"❌ Композиция '{compositionName}' не найдена!");
        player.ChatMessage("Используй: /htspawn для списка");
        return;
    }
    
    if (!TrainTrackSpline.TryFindTrackNear(player.transform.position, 1500f, out TrainTrackSpline trackSpline, out float distOnSpline))
    {
        player.ChatMessage("❌ Рельсы не найдены в радиусе 1500м");
        return;
    }
    
    float len = trackSpline.GetLength();
    string nm = trackSpline.name;
    
    if (len < config.MinTrackLength || 
        nm.IndexOf("3x36", System.StringComparison.OrdinalIgnoreCase) >= 0 || 
        nm.IndexOf("monument", System.StringComparison.OrdinalIgnoreCase) >= 0)
    {
        player.ChatMessage($"⚠️ Ближайший трек не годится ({nm}, {len:F0} м)");
        return;
    }
    
    if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
    {
        activeHellTrain.Kill();
        activeHellTrain = null;
    }
    
    player.ChatMessage($"✅ Спавним композицию: {compositionName}");
    
    SpawnTrainFromComposition(compositionName, trackSpline, distOnSpline);
}


[ChatCommand("helltrain")]
private void CmdHelltrain(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }

    if (args.Length == 0 || !args[0].Equals("startnear", System.StringComparison.OrdinalIgnoreCase))
    {
        player.ChatMessage("📋 Использование: /helltrain startnear [composition_name]");
        player.ChatMessage("Доступные композиции:");
        foreach (var key in config.Compositions.Keys)
        {
            var comp = config.Compositions[key];
            player.ChatMessage($"   • {key} ({comp.Tier}, {comp.Wagons.Count} вагонов)");
        }
        return;
    }
	// нормализуем первый аргумент команды
var arg = (args != null && args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty);

	if (arg == "stop")
    {
        ForceDestroyHellTrain();
        SendReply(player, "🚂 Helltrain остановлен и очищен.");
        return;
    }

    string compositionName = null;
    if (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
    {
        compositionName = args[1].ToLower();
        if (!config.Compositions.ContainsKey(compositionName))
        {
            player.ChatMessage($"❌ Композиция '{compositionName}' не найдена!");
            player.ChatMessage("Используй: /helltrain startnear для списка");
            return;
        }
    }
    else
    {
        var compositions = config.Compositions.Keys.ToList();
        if (compositions.Count == 0)
        {
            player.ChatMessage("❌ Нет композиций в конфиге!");
            return;
        }
        compositionName = compositions[_rng.Next(0, compositions.Count)];
        player.ChatMessage($"🎲 Выбрана случайная композиция: {compositionName}");
    }

    if (!TrainTrackSpline.TryFindTrackNear(player.transform.position, 1500f, out TrainTrackSpline trackSpline, out float distOnSpline))
    {
        player.ChatMessage("❌ Рельсы не найдены в радиусе 1500м");
        return;
    }

    float len = trackSpline.GetLength();
    string nm = trackSpline.name;
    
    if (len < config.MinTrackLength || 
        nm.IndexOf("3x36", System.StringComparison.OrdinalIgnoreCase) >= 0 || 
        nm.IndexOf("monument", System.StringComparison.OrdinalIgnoreCase) >= 0)
    {
        player.ChatMessage($"⚠️ Ближайший трек не годится ({nm}, {len:F0} м). Подойди ближе к кольцу и повтори.");
        return;
    }

    if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
    {
        activeHellTrain.Kill();
        activeHellTrain = null;
    }

    player.ChatMessage($"✅ Спавним композицию: {compositionName}");
    
    SpawnTrainFromComposition(compositionName, trackSpline, distOnSpline);
}



[ChatCommand("htpos")]
private void CmdGetPosition(BasePlayer player, string command, string[] args)
{
    var engine = GetNearestEngine(player);
    if (engine == null)
    {
        SendReply(player, "❌ Поезд далеко!");
        return;
    }
    
    Vector3 localPos = engine.transform.InverseTransformPoint(player.transform.position);
    
    SendReply(player, $"📍 Твоя позиция:");
    SendReply(player, $"World: {player.transform.position}");
    SendReply(player, $"Local (от поезда): {localPos}");
    
   // Puts($"📍 Игрок {player.displayName}: Local={localPos}");
}

[ConsoleCommand("cleanup.trains")]
private void CleanupTrains(ConsoleSystem.Arg arg)
{

    BasePlayer player = arg.Player();
    if (player != null && !player.IsAdmin)
    {
        SendReply(arg, "❌ Только для админов!");
        return;
    }

    bool onlyHellTrain = arg.Args != null && arg.Args.Length > 0 && arg.Args[0] == "hell";
    int count = 0;

    if (onlyHellTrain)
    {
        if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
        {
            ForceDestroyHellTrain();
            count = 1;
            SendReply(arg, $"🧹 Hell Train принудительно удалён");
        }
        else
        {
            SendReply(arg, "⚠️ Активного Hell Train нет");
        }
    }
    else
    {
        if (activeHellTrain != null && !activeHellTrain.IsDestroyed)
        {
            ForceDestroyHellTrain();
            count++;
        }

        var trains = UnityEngine.Object.FindObjectsOfType<TrainEngine>();
        
        foreach (var train in trains)
        {
            if (train != null && !train.IsDestroyed)
            {
                train.Kill();
                count++;
            }
        }
        
        SendReply(arg, $"🧹 Удалено поездов: {count}");
    }
    
  //  Puts($"🧹 Удалено поездов: {count} (admin: {player?.displayName ?? "RCON"})");
}

[ChatCommand("htcleanup")]
private void CmdHtCleanup(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }

    bool onlyHellTrain = args.Length > 0 && args[0].Equals("hell", System.StringComparison.OrdinalIgnoreCase);

    _suppressHooks = true;
    StopEngineWatchdog();
    StopGridCheckTimer();
    CancelLifecycleTimer();

    try
    {
        if (onlyHellTrain)
        {
            ForceDestroyHellTrain(); // внутренняя чистка своих списков
            player.ChatMessage("🧹 Hell Train принудительно удалён");
            return;
        }

        // глобально: безопасный снапшот
        var snapshot = Pool.GetList<TrainEngine>();
        foreach (var te in UnityEngine.Object.FindObjectsOfType<TrainEngine>())
            if (te != null && !te.IsDestroyed) snapshot.Add(te);

        int killed = 0;
        foreach (var te in snapshot) { te.Kill(); killed++; }
        Pool.FreeList(ref snapshot);

        // локальные трекеры
        _spawnedCars.Clear();
        _spawnedTrainEntities.Clear();
        _spawnedTurrets.Clear();
        _spawnedSamSites.Clear();
        _spawnedNPCs.Clear();
        _savedProtection.Clear();
        _explosionDamageArmed = false;
        _explodedOnce = false;
        activeHellTrain = null;
        _trainLifecycle = null;

        player.ChatMessage($"🧹 Удалено поездов: {killed}");
    }
    finally
    {
        _suppressHooks = false;
        _engineCleanupTriggered = false;
        _engineCleanupCooldownUntil = 0f;
    }
}


#endregion


        #region OXIDE.HOOKS

// ============================================
// ✅ ЕДИНСТВЕННЫЙ ХУК УРОНА - ОБЪЕДИНЁННЫЙ
// ============================================

// 1️⃣ CanEntityTakeDamage - FF защита + блок урона до _allowDestroy
private object CanEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
{
    if (entity == null || hitInfo == null) 
        return null;
    
    // Защита вагонов
    if (entity is TrainCar && _spawnedCars.Contains(entity))
    {
        if (!_allowDestroy)
        {
            hitInfo?.damageTypes?.Clear();
            return false;
        }
    }
    
    // Защита от FF
    var victimDefender = entity.GetComponent<HellTrainDefender>();
    if (victimDefender != null)
    {
        BaseEntity attacker = hitInfo.Initiator;
        
        if (attacker != null)
        {
            var attackerDefender = attacker.GetComponent<HellTrainDefender>();
            
            if (attackerDefender != null)
            {
                hitInfo.damageTypes.Clear();
                hitInfo.DoHitEffects = false;
                hitInfo.HitMaterial = 0;
                
                if (entity is AutoTurret turret)
                {
                    NextTick(() => {
                        if (turret != null && !turret.IsDestroyed && turret.target != null)
                        {
                            var targetDefender = turret.target.GetComponent<HellTrainDefender>();
                            if (targetDefender != null)
                                turret.SetTarget(null);
                        }
                    });
                }
                
                return false;
            }
        }
    }
    
    return null;
}

// ============================================
// ✅ ТУРЕЛЬ НЕ АТАКУЕТ СОЮЗНИКОВ
// ============================================

private object OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
{
    if (turret == null || target == null)
        return null;
    
    if (!_spawnedTurrets.Contains(turret)) 
        return null;
    
    var targetDefender = target.GetComponent<HellTrainDefender>();
    if (targetDefender != null)
    {
        NextTick(() => {
            if (turret != null && !turret.IsDestroyed)
                turret.SetTarget(null);
        });
        
        return false;
    }
    
    return null;
}

// ============================================
// ✅ КОДЛОК / ОТЦЕПЛЕНИЕ / ОСТАНОВКА
// ============================================

private object CanMountEntity(BasePlayer player, BaseMountable baseMountable)
{
    if (activeHellTrain == null || activeHellTrain.IsDestroyed)
        return null;
    
    TrainCar trainCar = baseMountable.VehicleParent() as TrainCar;
    if (trainCar && _spawnedCars.Contains(trainCar))
    {
        if (authorizedPlayers.Contains(player.userID))
            return null;
        
        player.ChatMessage("🔒 Введи код: /htcode 6666");
        return false;
    }

    return null;
}

private object OnTrainCarUncouple(TrainCar trainCar, BasePlayer player)
{
    if (trainCar && _spawnedCars.Contains(trainCar))
    {
        player.ChatMessage("⚠️ Нельзя отцепить вагоны Hell Train!");
        return false;
    }

    return null;
}

private object OnEngineStop(TrainEngine trainEngine)
{
    if (trainEngine && trainEngine == activeHellTrain)
        return false;

    return null;
}

// ============================================
// ✅ СТАНДАРТНЫЕ HOOKS
// ============================================



private readonly List<ulong> _tmpIds = new List<ulong>();


// ... остальные хуки без изменений

#endregion

#region HT.RAILWAY.SCAN

private void ScanRailwayNetwork()
{
    Puts("🔍 Сканируем железнодорожную сеть...");
    
    availableOverworldSplines.Clear();
    availableUnderworldSplines.Clear();
    
    // Используем ТОЛЬКО Path.Rails для кольцевых петель
    if (config.AllowAboveGround && TerrainMeta.Path != null && TerrainMeta.Path.Rails != null)
    {
        foreach (PathList pathList in TerrainMeta.Path.Rails)
        {
            if (pathList == null || pathList.Path == null) 
                continue;

            // ТОЛЬКО КОЛЬЦЕВЫЕ ПЕТЛИ!
            if (!pathList.Path.Circular)
            {
              //  Puts($"   ⚠️ Пропускаем линейный путь (не петля): {pathList.Name}");
                continue;
            }

            float totalLength = 0f;
            for (int i = 0; i < pathList.Path.Points.Length - 1; i++)
            {
                totalLength += Vector3.Distance(pathList.Path.Points[i], pathList.Path.Points[i + 1]);
            }
            
            if (totalLength < config.MinTrackLength)
            {
             //   Puts($"   ⚠️ Петля слишком короткая: {pathList.Name} ({totalLength:F0}м < {config.MinTrackLength:F0}м)");
                continue;
            }

         //   Puts($"   ✅ Найдена петля: {pathList.Name} ({totalLength:F0}м)");

            // Добавляем ВСЕ сплайны этой петли
            int skip = pathList.Path.Points.Length >= 1000 ? 10 : pathList.Path.Points.Length >= 500 ? 5 : 1;
            
            for (int i = 0; i < pathList.Path.Points.Length; i += skip)
            {
                Vector3 point = pathList.Path.Points[i];
                
                if (TrainTrackSpline.TryFindTrackNear(point, 10f, out TrainTrackSpline spline, out float dist))
                {
                    if (!availableOverworldSplines.Contains(spline))
                    {
                        availableOverworldSplines.Add(spline);
                    }
                }
            }
        }
    }
    
    // Подземка (опционально)
    if (config.AllowUnderGround)
    {
        TrainTrackSpline[] allSplines = UnityEngine.Object.FindObjectsOfType<TrainTrackSpline>();
        
        foreach (var spline in allSplines)
        {
            if (!spline || !spline.gameObject)
                continue;
                
            string name = spline.gameObject.name;
            
            if (name.StartsWith("train_tunnel"))
            {
                if (!config.AllowTransition && 
                    (name.Contains("transition_up", System.StringComparison.OrdinalIgnoreCase) || 
                     name.Contains("transition_down", System.StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                
                if (!availableUnderworldSplines.Contains(spline))
                {
                    availableUnderworldSplines.Add(spline);
                }
            }
        }
        
      //  Puts($"   ✅ Подземных треков: {availableUnderworldSplines.Count}");
    }
    
   // Puts($"✅ Найдено треков: {availableOverworldSplines.Count} наземных, {availableUnderworldSplines.Count} подземных");
}

#endregion

        #region HT.DEBUG
        private void DebugLog(string message)
        {
            Puts(message);
        }
        #endregion
		

#region HT.LAYOUT.OBJECTS

private void SpawnLayoutObjects(TrainCar trainCar, TrainLayout layout)
{
    if (layout.objects == null || layout.objects.Count == 0)
    {
        Puts($"   ⚠️ SpawnLayoutObjects({layout.name}): objects пуст! (null={layout.objects == null}, count={layout.objects?.Count ?? 0})");
        return;
    }
    
  //  Puts($"   🎯 Спавним {layout.objects.Count} объектов из {layout.name}...");
    
    ProtectionProperties turretProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
    turretProtection.density = 100;
    turretProtection.amounts = new float[] 
    { 
        1f, 1f, 1f, 1f, 1f, 0.8f, 1f, 1f, 1f, 0.9f,
        0.5f, 0.5f, 1f, 1f, 0f, 0.5f, 0f, 1f, 1f, 0f, 
        1f, 0.9f, 0f, 1f, 0f 
    };
    
    foreach (var obj in layout.objects)
    {
        Puts($"🔍 DEBUG: Спавним {obj.type}, npc_type={obj.npc_type ?? "null"}, gun={obj.gun ?? "null"}, kit={obj.kit ?? "null"}");
        
        Vector3 localPos = V3(obj.position);
        Quaternion localRot = Quaternion.Euler(0, obj.rotationY, 0);
        
        Vector3 worldPos = trainCar.transform.TransformPoint(localPos);
        Quaternion worldRot = trainCar.transform.rotation * localRot;
        
        string prefab = null;
        
        switch (obj.type?.ToLower())
{
    case "npc":
        prefab = SCIENTIST_PREFAB;
        break;
    case "turret":
        prefab = TURRET_PREFAB;
        break;
    case "samsite":
        prefab = SAMSITE_PREFAB;
        break;
case "loot":
{

    // фракция поезда/лэйаута
    string factionUpper = (layout?.faction ?? "BANDIT").ToUpper();

    // обычный (НЕ hack) префаб под фракцию
    string lootPrefab = GetCratePrefabForFaction(factionUpper);

    var ent = GameManager.server.CreateEntity(lootPrefab, worldPos, worldRot);
    if (ent == null)
    {
        Puts("❌ Не удалось создать лут-ящик (CreateEntity вернул null)");
        break;
    }

    ent.enableSaving = false;
    ent.SetParent(trainCar, false, false);
    ent.transform.localPosition = localPos;
    ent.transform.localRotation = localRot;

    // защита от физики
    var combat = ent as BaseCombatEntity;
    if (combat != null) combat.InitializeHealth(5000f, 5000f);
    var rb = ent.GetComponent<Rigidbody>();
    if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

    ent.Spawn();

    // учёт наших ящиков
    ulong id = ent.net.ID.Value;
    _ourCrates.Add(id);
    _crateStates[id] = CrateState.Idle;
    _crateFaction[id] = factionUpper;

    // назначение пресета A/B 50/50
    string presetKey = PickPresetAB(factionUpper);
    Puts($"   🎲 Применяю пресет: {presetKey}");

    bool presetApplied = false;
    var sc = ent as StorageContainer;
    if (Loottable != null && sc != null)
    {
        var ok = (bool)(Loottable.Call("AssignPreset", this, presetKey, sc) ?? false);
        presetApplied = ok;
        if (!ok)
            Puts($"   ⚠️ Не удалось применить пресет '{presetKey}' — проверь, что он создан и включён в Loottable UI (категория Helltrain).");
    }

    // fallback: если A/B не применился, пробуем то, что записано в объекте (preset/presets)
    if (!presetApplied && sc != null && Loottable != null)
    {
        string fallback = null;
        if (obj.presets != null && obj.presets.Length > 0)
            fallback = obj.presets[UnityEngine.Random.Range(0, obj.presets.Length)];
        else if (!string.IsNullOrEmpty(obj.preset))
            fallback = obj.preset;

        if (!string.IsNullOrEmpty(fallback))
        {
            var ok2 = (bool)(Loottable.Call("AssignPreset", this, fallback, sc) ?? false);
            if (ok2)
                Puts($"   ✅ Fallback пресет применён: {fallback}");
            else
                Puts($"   ⚠️ Fallback пресет '{fallback}' тоже не применился.");
        }
    }

    break;
}

        
        BaseEntity entity = GameManager.server.CreateEntity(prefab, worldPos, worldRot);
        if (entity == null) continue;
        
        entity.enableSaving = false;
        entity.Spawn();
        _spawnedTrainEntities.Add(entity);
		
		
        
        // --- АКТИВАЦИЯ В БОЕВОМ РЕЖИМЕ (runtime) ---
        var npcCast = entity as ScientistNPC;
        if (npcCast != null)
        {
            var brain = npcCast.GetComponent<BaseAIBrain>();
            if (brain != null) brain.enabled = true;

            var nav = npcCast.GetComponent<BaseNavigator>();
            if (nav != null)
            {
                nav.CanUseNavMesh = true;
                nav.SetDestination(npcCast.transform.position, BaseNavigator.NavigationSpeed.Normal, 0f);
                nav.ClearFacingDirectionOverride();
            }

            npcCast.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
            npcCast.InvalidateNetworkCache();
            npcCast.SendNetworkUpdateImmediate();
        }
        else
        {
            var at = entity as AutoTurret;
            if (at != null)
            {
                at.SetFlag(IOEntity.Flag_HasPower, true, false, true);
                at.UpdateFromInput(100, 0);
                at.SetFlag(BaseEntity.Flags.On, true, false, true);
                at.InvalidateNetworkCache();
                at.SendNetworkUpdateImmediate();
            }
            else
            {
                var samRT = entity as SamSite;
                if (samRT != null)
                {
                    samRT.SetFlag(IOEntity.Flag_HasPower, true, false, true);
                    samRT.SetFlag(BaseEntity.Flags.On, true, false, true);
                    samRT.InvalidateNetworkCache();
                    samRT.SendNetworkUpdateImmediate();
                }
            }
        }


		
		

        if (entity is AutoTurret turret)
            _spawnedTurrets.Add(turret);
        else if (entity is SamSite samSite)
            _spawnedSamSites.Add(samSite);
        else if (entity is ScientistNPC npcRT)
            _spawnedNPCs.Add(npcRT);
        
        NextTick(() =>
        {
            if (entity == null || entity.IsDestroyed || trainCar == null || trainCar.IsDestroyed)
                return;
            
            bool shouldParent = !(entity is ScientistNPC);
            
            if (shouldParent)
            {
                entity.SetParent(trainCar, false, false);
                entity.transform.localPosition = localPos;
                entity.transform.localRotation = localRot;
                entity.SendNetworkUpdate();
            }
            
            if (entity is AutoTurret turret)
            {
                var turretComponent = turret.gameObject.AddComponent<TrainAutoTurret>();
                turretComponent.plugin = this;
                
                if (!string.IsNullOrEmpty(obj.gun))
                {
                    timer.Once(2.0f, () =>
                    {
                        if (turret == null || turret.IsDestroyed)
                            return;

                        GiveTurretWeapon(turret, obj.gun, obj.ammo, obj.ammo_count);
                    });
                }
            }
            else if (entity is SamSite samRT)
            {
                samRT.gameObject.AddComponent<TrainSamSite>();
            }
            else if (entity is ScientistNPC npc)
{
    npc.gameObject.AddComponent<HellTrainDefender>();
    
    BaseAIBrain brain = npc.GetComponent<BaseAIBrain>();
    if (brain != null)
    {
        brain.enabled = true;
        brain.ForceSetAge(0);
    }
    
    var marker = npc.gameObject.AddComponent<NPCTypeMarker>();
    marker.npcType = obj.npc_type;
	marker.savedKit = obj.kit;                              // сохранить кит из JSON
marker.savedKits = obj.kits != null ? new List<string>(obj.kits) : new List<string>();

    
    // ✅ КРИТИЧНО: Захватываем obj в локальную переменную!
    ObjSpec capturedObj = obj;
    
    timer.Once(1.0f, () =>
    {
        if (npc == null || npc.IsDestroyed || npc.inventory == null)
            return;

        Puts($"   🎯 Выдаём предметы NPC ({marker.npcType})...");
        GiveNPCItems(npc, capturedObj);  // ← Используем ЗАХВАЧЕННЫЙ obj!
    });
}
            
            Puts($"   🎯 Заспавнен: {obj.type} на {trainCar.ShortPrefabName}");
        });
    }
}
}

private string GetKitForNPC(ObjSpec obj)
{
    if (!string.IsNullOrEmpty(obj.kit))
        return obj.kit;
    
    if (obj.kits != null && obj.kits.Count > 0)
    {
        int index = _rng.Next(0, obj.kits.Count);
        return obj.kits[index];
    }
       
    return null;
}

private void GiveTurretWeapon(AutoTurret turret, string gun, string ammo, int ammoCount)
{
    if (turret == null || turret.IsDestroyed || turret.inventory == null)
    {
        PrintWarning($"❌ GiveTurretWeapon: turret недоступна!");
        return;
    }
    
    Puts($"🔧 Выдаём оружие турели: gun={gun}, ammo={ammo}, count={ammoCount}");
    
    turret.inventory.Clear();
    ItemManager.DoRemoves();
    
    string weaponShortname = gun?.ToLower();
    if (string.IsNullOrEmpty(weaponShortname))
        weaponShortname = "lmg.m249";

    switch (weaponShortname)
    {
        case "m249": weaponShortname = "lmg.m249"; break;
        case "ak": weaponShortname = "rifle.ak"; break;
        case "lr300":
        case "lr": weaponShortname = "rifle.lr300"; break;
        case "mp5": weaponShortname = "smg.mp5"; break;
    }

    var weaponDef = ItemManager.FindItemDefinition(weaponShortname);
    if (weaponDef == null)
    {
        PrintWarning($"❌ Не найден ItemDefinition: {weaponShortname}");
        return;
    }
    
    var weaponItem = ItemManager.Create(weaponDef, 1, 0);
    if (weaponItem == null || !weaponItem.MoveToContainer(turret.inventory, 0, true))
    {
        PrintWarning($"❌ Не удалось добавить оружие!");
        weaponItem?.Remove();
        return;
    }
    
    Puts($"   ✅ Оружие добавлено в слот 0");
    
    if (string.IsNullOrEmpty(ammo))
        ammo = "ammo.rifle";
    
    if (ammoCount <= 0)
        ammoCount = 500;
    
    var ammoDef = ItemManager.FindItemDefinition(ammo);
    if (ammoDef != null)
    {
        var ammoItem = ItemManager.Create(ammoDef, ammoCount, 0);
        if (ammoItem != null && ammoItem.MoveToContainer(turret.inventory, 1, true))
        {
            Puts($"   ✅ Патроны добавлены в слот 1");
        }
        else
        {
            ammoItem?.Remove();
        }
    }
    
    NextTick(() => 
    {
        if (turret == null || turret.IsDestroyed)
            return;
        
        turret.UpdateAttachedWeapon();
        turret.UpdateTotalAmmo();
        turret.SendNetworkUpdate();
        
        Puts($"   ✅ Турель готова к бою!");
    });
}

private void GiveNPCItems(ScientistNPC npc, ObjSpec obj)
{
    if (npc == null || npc.inventory == null)
    {
        Puts("❌ NPC или инвентарь null!");
        return;
    }

    // 1) Сначала полностью чистим дефолтный лут у NPC (убираем синий хазмат и т.д.)
    npc.inventory.Strip();

    // 2) Кит берём ТОЛЬКО из obj.kit / obj.kits (НИКАК НЕ ИЗ npcType!)
    string kitName = GetKitForNPC(obj);

    Puts("════════════════════════════════════════");
    Puts($"🎯 GiveNPCItems:");
    Puts($"   obj.kit = '{obj?.kit ?? "NULL"}'");
    Puts($"   obj.kits.Count = {obj?.kits?.Count ?? 0}");
    Puts($"   выбранный kitName = '{kitName ?? "NULL"}'");
    Puts("════════════════════════════════════════");

    if (string.IsNullOrEmpty(kitName))
    {
        Puts("⚠️ Кит не задан в лэйауте (obj.kit/obj.kits пусто) — ничего не выдаю, чтобы не было рандом-хазмата.");
        return;
    }

    // 3) Выдаём кит через KitsSuite
    var result = KitsSuite?.Call("GiveKit", (BaseEntity)npc, kitName);
    Puts($"📞 KitsSuite.GiveKit('{kitName}') => {result} (тип: {result?.GetType().Name ?? "null"})");
	timer.Once(0.25f, () =>
{
    if (npc == null || npc.IsDestroyed || npc.inventory == null) return;
    // если одежда лежит в main — перекинем в wear
    foreach (var it in npc.inventory.containerMain.itemList.ToArray())
        if (it.info.category == ItemCategory.Attire)
            it.MoveToContainer(npc.inventory.containerWear, -1, true);
    npc.SendNetworkUpdate();
});


    // 4) Проверка/добивка через 1.0с: активируем оружие и убеждаемся, что броня в wear
    timer.Once(1.0f, () =>
    {
        if (npc == null || npc.IsDestroyed || npc.inventory == null)
            return;

        // Активируем первое оружие на поясе, если есть
        Item firstWeapon = null;
        if (npc.inventory?.containerBelt != null)
        {
            foreach (var item in npc.inventory.containerBelt.itemList)
            {
                if (item?.GetHeldEntity() is BaseProjectile)
                {
                    firstWeapon = item;
                    break;
                }
            }
        }
        if (firstWeapon != null)
            npc.UpdateActiveItem(firstWeapon.uid);

        // Если по какой-то причине весь инвентарь пуст — НИКАКИХ фолбеков на синий хазмат,
        // лучше оставить пустым, чтобы сразу видно было проблему кита.
        int total =
            (npc.inventory.containerMain?.itemList?.Count ?? 0) +
            (npc.inventory.containerBelt?.itemList?.Count ?? 0) +
            (npc.inventory.containerWear?.itemList?.Count ?? 0);

        if (total == 0)
            Puts($"❌ Кит '{kitName}' ничего не выдал (инвентарь пуст). Проверь пресет в KitsSuite.");
    });
}

private Item GiveItem(ScientistNPC npc, string shortname, int amount, ulong skin, string containerName)
{
    var def = ItemManager.FindItemDefinition(shortname);
    if (def == null)
    {
        Puts($"   ❌ Не найден ItemDefinition: {shortname}");
        return null;
    }
    
    var item = ItemManager.Create(def, amount, skin);
    if (item == null)
    {
        Puts($"   ❌ Не удалось создать Item: {shortname}");
        return null;
    }
    
    ItemContainer container = null;
    
    switch (containerName)
    {
        case "wear":
            container = npc.inventory.containerWear;
            break;
        case "belt":
            container = npc.inventory.containerBelt;
            break;
        default:
            container = npc.inventory.containerMain;
            break;
    }
    
    if (container == null || !item.MoveToContainer(container, -1, true))
    {
        item.Remove();
        Puts($"   ❌ Не удалось переместить {shortname} в {containerName}");
        return null;
    }
    
    return item;
}




#endregion


#region HT.LOOT.LOOTTABLE


private string GetRandomLootPreset(string faction)
{
    switch ((faction ?? "BANDIT").ToUpper())
    {
        case "PMC":    return UnityEngine.Random.value < 0.5f ? "pmc_weapon"    : "pmc_other";
        case "COBLAB": return UnityEngine.Random.value < 0.5f ? "coblab_weapon" : "coblab_medeat";
        default:       return UnityEngine.Random.value < 0.5f ? "bandit_weapon" : "bandit_medeat";
    }
}

private string QualifyLtPreset(string name)
{
    return name != null && !name.StartsWith("Helltrain_", StringComparison.OrdinalIgnoreCase)
        ? $"Helltrain_{name}"
        : name;
}



// ЗАМЕНИ оба объявления TryAssignLoottable на этот ОДИН метод
private void TryAssignLoottable(ItemContainer container, string preset)
{
    if (container == null || string.IsNullOrEmpty(preset)) return;
    if (!plugins.Exists("Loottable") || Loottable == null) return;

    Puts($"   🎲 Применяю пресет: {preset}");
    bool ok = false;

    var r1 = Loottable.Call("AssignPreset", this, preset, container);
    if (r1 is bool b1 && b1) ok = true;

    if (!ok)
    {
        Loottable.Call("PopulateContainer", this, preset, container);
        ok = container.itemList != null && container.itemList.Count > 0;
    }
    if (!ok)
    {
        Loottable.Call("ApplyPreset", this, preset, container);
        ok = container.itemList != null && container.itemList.Count > 0;
    }

    if (!ok)
        PrintWarning($"   ⚠️ Не удалось применить пресет '{preset}' — проверь точное имя в Loottable UI.");
}



#endregion


        #region РЕДАКТОР ЛЭЙАУТОВ

private readonly Hash<ulong, WagonEditor> m_WagonEditors = new Hash<ulong, WagonEditor>();
// === HELLTRAIN CRATES SYSTEM ===
private readonly List<ulong> _ourCrates = new List<ulong>();
private readonly Dictionary<ulong, CrateState> _crateStates = new Dictionary<ulong, CrateState>();
private readonly Dictionary<ulong, string> _crateFaction = new Dictionary<ulong, string>();


[ChatCommand("htreload")]
private void CmdHtReload(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }
    
    player.ChatMessage("🔄 ПОЛНАЯ перезагрузка всех лэйаутов...");
    
    // Очищаем кеш и перезагружаем всё
    _layouts.Clear();
    LoadLayouts();
    
    player.ChatMessage($"✅ Лэйауты перезагружены! Найдено: {_layouts.Count}");
    
    foreach (var kv in _layouts)
    {
        int objCount = kv.Value.objects?.Count ?? 0;
        player.ChatMessage($"   • {kv.Key}: {objCount} объектов");
    }
}

[ChatCommand("htedit")]
private void CmdHtEdit(BasePlayer player, string command, string[] args)
{
    if (!player.IsAdmin)
    {
        player.ChatMessage("❌ Только для админов!");
        return;
    }

    if (args.Length == 0)
    {
        player.ChatMessage("📋 Команды редактора:");
        player.ChatMessage("/htedit load <layoutName> - Открыть лэйаут");
        player.ChatMessage("/htedit save - Сохранить изменения");
        player.ChatMessage("/htedit cancel - Закрыть без сохранения");
        
        if (m_WagonEditors.ContainsKey(player.userID))
        {
            player.ChatMessage("/htedit move - Переместить объект (смотри на него)");
            player.ChatMessage("/htedit spawn <type> - Создать npc/turret/bradley/samsite/loot");
            player.ChatMessage("/htedit delete - Удалить объект (смотри на него)");
            player.ChatMessage("");
            player.ChatMessage("💡 ЛКМ - разместить | ПКМ - отмена | RELOAD - поворот");
        }
        return;
    }

    m_WagonEditors.TryGetValue(player.userID, out WagonEditor wagonEditor);

    switch (args[0].ToLower())
{
    case "load":
{
    if (wagonEditor)
    {
        player.ChatMessage("⚠️ Сначала закрой текущий редактор: /htedit save или /htedit cancel");
        return;
    }

    if (args.Length != 2)
    {
        player.ChatMessage("❌ Укажи имя: /htedit load wagonC_pmc");
        player.ChatMessage("📋 Доступные:");
        var dir = Path.Combine(Interface.Oxide.DataDirectory, "Helltrain/Layouts");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var files = Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x)
            .ToList();

        if (files.Count == 0)
        {
            player.ChatMessage("❌ Нет лэйаутов в oxide/data/Helltrain/Layouts/");
        }
        else
        {
            player.ChatMessage("📂 Доступные лэйауты:\n" + string.Join(", ", files));
        }
        return;
    }

    LoadLayouts(); // ← перечитываем лэйауты
    string layoutName = args[1].ToLower();

    if (!_layouts.ContainsKey(layoutName))
    {
        player.ChatMessage($"❌ Лэйаут '{layoutName}' не найден даже после обновления. Проверь имя файла в oxide/data/Helltrain/Layouts/");
        return;
    }

    if (!TrainTrackSpline.TryFindTrackNear(player.transform.position, 20f, out TrainTrackSpline spline, out float dist))
    {
        player.ChatMessage("⚠️ Рельсы не найдены! Подойди ближе к ним.");
        return;
    }

    var layout = _layouts[layoutName];
	if (string.IsNullOrEmpty(layout.name))
{
    layout.name = layoutName;
    Interface.Oxide.DataFileSystem.WriteObject($"Helltrain/Layouts/{layout.name}", layout, true);
}

    if (layout == null)
    {
        player.ChatMessage($"❌ Лэйаут '{args[1]}' не найден!");
        return;
    }

    // загрузка в редактор
    Vector3 pos = spline.GetPosition(dist);
    Vector3 fwd = spline.GetTangentCubicHermiteWorld(dist);
    Quaternion rot = fwd.magnitude > 0 ? Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

    string prefab = WagonPrefabC;
    if (layout.cars != null && layout.cars.Count > 0)
        prefab = GetWagonPrefabByVariant(layout.cars[0].variant);

    TrainCar trainCar = GameManager.server.CreateEntity(prefab, pos, rot) as TrainCar;
    trainCar.enableSaving = true;
    trainCar.frontCoupling = null;
    trainCar.rearCoupling = null;
    trainCar.platformParentTrigger.ParentNPCPlayers = true;
    trainCar.Spawn();

    wagonEditor = player.gameObject.AddComponent<WagonEditor>();
    wagonEditor.Load(trainCar, layout, this);
    m_WagonEditors[player.userID] = wagonEditor;

    player.ChatMessage($"✅ Редактор: {args[1]}");
    player.ChatMessage($"📦 Загружено объектов: {layout.objects?.Count ?? 0}");
    return;
}



    case "save":
    {
        if (!wagonEditor)
        {
            player.ChatMessage("❌ Редактор не открыт!");
            return;
        }

        wagonEditor.Save();
        UnityEngine.Object.Destroy(wagonEditor);

        m_WagonEditors.Remove(player.userID);
        player.ChatMessage("✅ Сохранено и закрыто");
        return;
    }

    case "cancel":
    {
        if (!wagonEditor)
        {
            player.ChatMessage("❌ Редактор не открыт!");
            return;
        }

        UnityEngine.Object.Destroy(wagonEditor);

        m_WagonEditors.Remove(player.userID);
        player.ChatMessage("✅ Редактор закрыт без сохранения");
        return;
    }

    case "move":
    {
        if (!wagonEditor)
        {
            player.ChatMessage("❌ Редактор не открыт!");
            return;
        }

        BaseEntity baseEntity = WagonEditor.FindEntityFromRay(player);
        if (!baseEntity || !wagonEditor.IsTrainEntity(baseEntity))
        {
            player.ChatMessage("❌ Это не объект редактора!");
            return;
        }

        wagonEditor.StartEditingEntity(baseEntity, false);
        return;
    }

    case "spawn":
{
    if (!wagonEditor)
    {
        player.ChatMessage("❌ Редактор не открыт!");
        return;
    }

    if (args.Length < 2)
    {
        player.ChatMessage("❌ Использование:");
        player.ChatMessage("/htedit spawn turret [gun] [ammo] [count]");
        player.ChatMessage("   Примеры: minigun | m249 | ak");
        player.ChatMessage("/htedit spawn samsite");
        player.ChatMessage("/htedit spawn loot [секунды]");
        player.ChatMessage("   Пример: /htedit spawn loot 5");
        player.ChatMessage("/htedit spawn npc <тип>");
        player.ChatMessage("   Типы: pmcshturm, pmcjuggernaut, coblabmain, banditmain");
        return;
    }

    string entityType = args[1].ToLower();
    string npcType = null;
    string gun = null;
    string ammo = null;
    int ammoCount = 0;
    float hackTimer = 0;
    string entityPrefab = null;

    // === Лут-ящик (обычный под фракцию лэйаута) ===
    if (entityType == "loot")
    {
        string faction = (wagonEditor != null ? wagonEditor.CurrentFaction : "BANDIT");
        entityPrefab = GetCratePrefabForFaction(faction);
        if (args.Length >= 3 && float.TryParse(args[2], out var ht))
            hackTimer = Mathf.Clamp(ht, 0f, 3600f);
    }
    else
    {
        if (entityType == "npc")
            npcType = args.Length >= 3 ? args[2] : "default";

        if (entityType == "turret")
        {
            gun = (args.Length >= 3 ? args[2] : "m249");
            ammo = (args.Length >= 4 ? args[3] : "ammo.rifle");
            ammoCount = (args.Length >= 5 && int.TryParse(args[4], out var c) ? c : 500);
        }

        entityPrefab = GetPrefabByType(entityType);
    }

    if (entityPrefab == null)
    {
        player.ChatMessage("❌ Неизвестный тип!");
        return;
    }

    Vector3 worldPos = player.transform.position + (player.eyes.BodyForward() * 3f);

    BaseEntity baseEntity = wagonEditor.CreateChildEntity(
        entityPrefab,
        wagonEditor.TrainCar.transform.InverseTransformPoint(worldPos),
        Quaternion.identity,
        npcType,
        gun, ammo, ammoCount,
        hackTimer
    );

    if (!baseEntity)
    {
        player.ChatMessage("❌ Не удалось создать!");
        return;
    }

    // ← КРИТИЧНО: объект добавляется в список детей редактора
    wagonEditor.GetChildren().Add(baseEntity);

    wagonEditor.StartEditingEntity(baseEntity, true);

    if (npcType != null)
        player.ChatMessage($"✅ NPC: {npcType}");
    else if (gun != null)
        player.ChatMessage($"✅ Турель: {gun} ({ammo} x{ammoCount})");
    else if (hackTimer > 0)
        player.ChatMessage($"✅ Ящик с таймером: {hackTimer}с");
    else
        player.ChatMessage($"✅ Создан: {entityType}");

    return;
}



    case "delete":
    {
        if (!wagonEditor)
        {
            player.ChatMessage("❌ Редактор не открыт!");
            return;
        }

        BaseEntity baseEntity = WagonEditor.FindEntityFromRay(player);
        if (!baseEntity || !wagonEditor.IsTrainEntity(baseEntity))
        {
            player.ChatMessage("❌ Это не объект редактора!");
            return;
        }

        wagonEditor.DeleteWagonEntity(baseEntity);
        player.ChatMessage($"✅ Удалён: {baseEntity.ShortPrefabName}");
        return;
    }

    default:
        player.ChatMessage("❌ Неизвестная команда! Используй /htedit для списка");
        return;
}
}

class WagonEditor : MonoBehaviour
{
    private BasePlayer m_Player;
	private bool m_IsLoading = false;
    private TrainLayout m_Layout;
    private TrainCar m_TrainCar;
    private Helltrain m_Plugin;
    private List<BaseEntity> m_Children = new List<BaseEntity>();
public string CurrentFaction => (m_Layout?.faction ?? "BANDIT").ToUpper();

    private BaseEntity m_CurrentEntity;
    private Construction m_Construction;
    private Vector3 m_RotationOffset = Vector3.zero;
    private int m_NextRotateFrame;
    private int m_NextClickFrame;
    private Vector3 m_StartPosition;
    private Quaternion m_StartRotation;

    public TrainCar TrainCar => m_TrainCar;
    public List<BaseEntity> GetChildren() => m_Children;

    private static ProtectionProperties _fullProtection;
    private static readonly RaycastHit[] RaycastBuffer = new RaycastHit[32];

    private void Awake()
    {
        m_Player = GetComponent<BasePlayer>();

        if (!_fullProtection)
        {
            _fullProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
            _fullProtection.density = 100;
            _fullProtection.amounts = new float[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        }
    }

    private void OnDestroy()
    {
        foreach (BaseEntity baseEntity in m_Children)
        {
            if (!baseEntity || baseEntity.IsDestroyed)
                continue;

            baseEntity.Kill(BaseNetworkable.DestroyMode.None);
        }

        m_Children.Clear();

        if (m_TrainCar && !m_TrainCar.IsDestroyed)
            m_TrainCar.Kill(BaseNetworkable.DestroyMode.None);
    }

 public void Load(TrainLayout layout)
{
    if (layout == null)
    {
        m_Player.ChatMessage("❌ Пустой лэйаут — загружать нечего");
        return;
    }

    m_IsLoading = true;
    m_Layout = layout;

    // Сносим текущие редакторские объекты
    foreach (var child in m_Children)
    {
        if (child && !child.IsDestroyed)
            child.Kill(BaseNetworkable.DestroyMode.None);
    }
    m_Children.Clear();

    if (layout.objects == null || layout.objects.Count == 0)
    {
        m_IsLoading = false;
        m_Player.ChatMessage("⚠️ В лэйауте нет объектов");
        return;
    }

    // Спавним со снапшота, чтобы не ловить 'Collection was modified'
    var snapshot = new List<ObjSpec>(layout.objects);
    foreach (var obj in snapshot)
    {
        // Определяем префаб
        string prefab = null;
        if (string.Equals(obj.type, "loot", StringComparison.OrdinalIgnoreCase))
        {
            string faction = !string.IsNullOrEmpty(obj.faction) ? obj.faction : (layout.faction ?? "BANDIT");
            prefab = (m_Plugin != null) ? m_Plugin.GetCratePrefabForFaction(faction) : PREFAB_CRATE_BANDIT;
        }
        else
        {
            prefab = (m_Plugin != null) ? m_Plugin.GetPrefabByType(obj.type) : null;
        }
        if (string.IsNullOrEmpty(prefab))
            continue;

        // Локальные трансформы из ObjSpec
        var localPos = new Vector3(
            obj.position != null && obj.position.Length >= 3 ? obj.position[0] : 0f,
            obj.position != null && obj.position.Length >= 3 ? obj.position[1] : 0f,
            obj.position != null && obj.position.Length >= 3 ? obj.position[2] : 0f
        );
        var localRot = Quaternion.Euler(0f, obj.rotationY, 0f);

        // ВАЖНО: во время Load не пишем в layout → addToLayout = false
        var ent = this.CreateChildEntity(
    prefab,
    localPos,
    localRot,
    obj.npc_type,
    obj.gun,
    obj.ammo,
    obj.ammoCount
);
        if (ent == null)
            continue;

        // Восстановление метаданных (NPC / Турель)
        if (!string.IsNullOrEmpty(obj.npc_type))
        {
            var npcMarker = ent.GetComponent<NPCTypeMarker>();
            if (npcMarker != null) npcMarker.npcType = obj.npc_type;
        }
        if (!string.IsNullOrEmpty(obj.gun))
        {
            var tmarker = ent.GetComponent<TurretMarker>();
            if (tmarker != null)
            {
                tmarker.gun = obj.gun;
                tmarker.ammo = obj.ammo;
                tmarker.ammoCount = obj.ammoCount;
            }
        }

        m_Children.Add(ent);
    }

// --- MASS FREEZE AFTER LOAD ---
foreach (var child in m_Children)
{
    if (!child || child.IsDestroyed) continue;

    if (child is ScientistNPC npc)
    {
        var brain = npc.GetComponent<BaseAIBrain>();
        if (brain != null) brain.enabled = false;

        var nav = npc.GetComponent<BaseNavigator>();
        if (nav != null)
        {
            nav.SetDestination(npc.transform.position, BaseNavigator.NavigationSpeed.Slow, 0f);
            nav.CanUseNavMesh = false;
            nav.ClearFacingDirectionOverride();
        }
        npc.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);
        npc.SendNetworkUpdate();
    }
    else if (child is AutoTurret at)
    {
        at.SetFlag(IOEntity.Flag_HasPower, false, false, true);
        at.SetFlag(BaseEntity.Flags.On, false, false, true);
        at.SetTarget(null);
        at.CancelInvoke(at.ServerTick);
        at.CancelInvoke(at.SendAimDir);
        at.CancelInvoke(at.ScheduleForTargetScan);
        at.SendNetworkUpdate();
    }
    else if (child is SamSite sam)
    {
        sam.SetFlag(IOEntity.Flag_HasPower, false, false, true);
        sam.SetFlag(BaseEntity.Flags.On, false, false, true);
        sam.CancelInvoke(sam.TargetScan);
        sam.SendNetworkUpdate();
    }
}
// --- /MASS FREEZE AFTER LOAD ---


    m_IsLoading = false;
    m_Player.ChatMessage($"✅ Загружено объектов: {m_Children.Count}");
}







// === Перегрузка для вызова /htedit load ===
public void Load(TrainCar trainCar, TrainLayout layout, Helltrain plugin)
{
    if (trainCar == null)
    {
        m_Player.ChatMessage("❌ Нет вагона для загрузки лэйаута");
        return;
    }
    if (layout == null || layout.objects == null)
    {
        m_Player.ChatMessage("❌ Пустой лэйаут — загружать нечего");
        return;
    }
    m_TrainCar = trainCar;
    m_Plugin = plugin;
    Load(layout);
}




  public void Save()
{
    if (m_TrainCar == null || m_Layout == null || string.IsNullOrEmpty(m_Layout.name))
    {
        m_Player.ChatMessage("❌ Нет вагона или имени лэйаута для сохранения");
        return;
    }

    var newObjects = new List<ObjSpec>();
    foreach (var child in m_Children)
    {
        if (child == null || child.IsDestroyed) continue;

        string type = (m_Plugin != null) ? m_Plugin.GetObjectType(child) : "unknown";
        if (string.IsNullOrEmpty(type) || type == "unknown") continue;

        Vector3 lp = m_TrainCar.transform.InverseTransformPoint(child.transform.position);
        float rotY = child.transform.localRotation.eulerAngles.y;

        string npcType = null, gun = null, ammo = null;
        int ammoCount = 0;

        var n = child.GetComponent<NPCTypeMarker>();
        if (n != null) npcType = n.npcType;

        var t = child.GetComponent<TurretMarker>();
        if (t != null)
        {
            gun = t.gun;
            ammo = t.ammo;
            ammoCount = t.ammoCount;
        }

        newObjects.Add(new ObjSpec {
            type = type,
            position = new float[] { lp.x, lp.y, lp.z },
            rotationY = rotY,
            npc_type = npcType,
            gun = gun,
            ammo = ammo,
            ammoCount = ammoCount,
            faction = this.CurrentFaction
        });
    }

    // КЛЮЧЕВОЕ: полная замена списка
    m_Layout.objects = newObjects;

    string dataKey = $"Helltrain/Layouts/{m_Layout.name}";
    Interface.Oxide.DataFileSystem.WriteObject(dataKey, m_Layout, true);

    m_Player.ChatMessage($"💾 Сохранено: {newObjects.Count} объектов → {m_Layout.name}.json");
}

private void WriteAutosave()
{
    var snapshot = new TrainLayout { objects = new List<ObjSpec>() };
    foreach (var child in m_Children)
    {
        if (child == null || child.IsDestroyed) continue;
        Vector3 localPos = m_TrainCar.transform.InverseTransformPoint(child.transform.position);
        float rotY = child.transform.localRotation.eulerAngles.y;
        snapshot.objects.Add(new ObjSpec {
            type = (m_Plugin != null) ? m_Plugin.GetObjectType(child) : null,
            position = new float[] { localPos.x, localPos.y, localPos.z },
            rotationY = rotY
        });
    }
    Interface.Oxide.DataFileSystem.WriteObject("Helltrain/Layouts/_editor_autosave", snapshot, true);
    m_Player.ChatMessage("💾 Сохранено в _editor_autosave.json (нет имени лэйаута)");
}






    public static BaseEntity FindEntityFromRay(BasePlayer player)
    {
        const int LAYERS = (1 << 0) | (1 << 8) | (1 << 10) | (1 << 17) | (1 << 26);

        int hits = Physics.RaycastNonAlloc(player.eyes.HeadRay(), RaycastBuffer, 10f, LAYERS, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            BaseEntity baseEntity = RaycastBuffer[i].collider.GetComponentInParent<BaseEntity>();
            if (!baseEntity || baseEntity.IsDestroyed)
                continue;

            if (baseEntity is TrainCar)
                continue;

            return baseEntity;
        }

        return null;
    }

    public bool IsTrainEntity(BaseEntity baseEntity) => m_Children.Contains(baseEntity);

    public void StartEditingEntity(BaseEntity baseEntity, bool justSpawned)
    {
        if (!justSpawned)
        {
            m_StartPosition = baseEntity.transform.localPosition;
            m_StartRotation = baseEntity.transform.localRotation;
        }

        m_CurrentEntity = baseEntity;

        m_Construction = PrefabAttribute.server.Find<Construction>(m_CurrentEntity.prefabID);
        if (!m_Construction)
        {
            m_Construction = new Construction();
            m_Construction.rotationAmount = new Vector3(0, 90f, 0);
            m_Construction.fullName = m_CurrentEntity.PrefabName;
            m_Construction.maxplaceDistance = 4f;
            m_Construction.canRotateBeforePlacement = m_Construction.canRotateAfterPlacement = true;
        }

        m_Player.ChatMessage($"📦 Редактируем: <color=#ce422b>{m_CurrentEntity.ShortPrefabName}</color>");
        m_Player.ChatMessage("🖱️ ЛКМ - разместить | ПКМ - отмена | RELOAD - поворот");
    }

    public void DeleteWagonEntity(BaseEntity baseEntity)
    {
        if (baseEntity == m_CurrentEntity)
            m_CurrentEntity = null;

        m_Children.Remove(baseEntity);
        baseEntity.Kill(BaseNetworkable.DestroyMode.None);
    }

    private void Update()
    {
        if (!m_CurrentEntity)
        {
            if (m_Player.serverInput.WasJustPressed(BUTTON.FIRE_PRIMARY) && Time.frameCount > m_NextClickFrame)
            {
                BaseEntity baseEntity = FindEntityFromRay(m_Player);
                if (baseEntity && IsTrainEntity(baseEntity))
                    StartEditingEntity(baseEntity, false);

                m_NextClickFrame = Time.frameCount + 20;
            }

            return;
        }

        Construction.Target target = new Construction.Target()
        {
            ray = m_Player.eyes.BodyRay(),
            player = m_Player,
            buildingBlocked = false,
        };

        UpdatePlacement(ref target);

        UpdateNetworkTransform();

        if (m_Player.serverInput.WasJustReleased(BUTTON.RELOAD) && Time.frameCount > m_NextRotateFrame)
        {
            if (m_Player.serverInput.IsDown(BUTTON.DUCK))
                m_RotationOffset.z = Mathf.Repeat(m_RotationOffset.z + 90f, 360);
            else if (m_Player.serverInput.IsDown(BUTTON.SPRINT))
                m_RotationOffset.x = Mathf.Repeat(m_RotationOffset.x + 90f, 360);
            else
                m_RotationOffset.y = Mathf.Repeat(m_RotationOffset.y + 90f, 360);

            m_NextRotateFrame = Time.frameCount + 20;
            m_Player.ChatMessage($"🔄 Поворот: X={m_RotationOffset.x:F0}° Y={m_RotationOffset.y:F0}° Z={m_RotationOffset.z:F0}°");
        }

        if (m_Player.serverInput.WasJustPressed(BUTTON.FIRE_PRIMARY) && Time.frameCount > m_NextClickFrame)
        {
            Vector3 finalLocalPos = m_TrainCar.transform.InverseTransformPoint(m_CurrentEntity.transform.position);
            m_Player.ChatMessage($"✅ Размещён: <color=#ce422b>{m_CurrentEntity.ShortPrefabName}</color>");
            m_Player.ChatMessage($"   Local: {finalLocalPos}");

            m_CurrentEntity = null;
            m_RotationOffset = Vector3.zero;
            m_NextClickFrame = Time.frameCount + 20;
        }
        else if (m_Player.serverInput.WasJustPressed(BUTTON.FIRE_SECONDARY))
        {
            m_Player.ChatMessage($"❌ Отменено: <color=#ce422b>{m_CurrentEntity.ShortPrefabName}</color>");

            if (m_StartPosition != Vector3.zero && m_StartRotation != Quaternion.identity)
            {
                m_CurrentEntity.transform.localPosition = m_StartPosition;
                m_CurrentEntity.transform.localRotation = m_StartRotation;

                UpdateNetworkTransform();
            }
            else
            {
                m_Children.Remove(m_CurrentEntity);
                m_CurrentEntity.Kill(BaseNetworkable.DestroyMode.None);
            }

            m_CurrentEntity = null;
            m_RotationOffset = Vector3.zero;
        }
    }
	



    public BaseEntity CreateChildEntity(
    string prefab, 
    Vector3 position, 
    Quaternion rotation, 
    string npcType = null,
    string gun = null,
    string ammo = null,
    int ammoCount = 0,
    float hackTimer = 0
)
{
    if (m_TrainCar == null || string.IsNullOrEmpty(prefab))
        return null;

    // создаём энтити в мировых координатах, но с лок.привязкой к вагону
    BaseEntity baseEntity = GameManager.server.CreateEntity(prefab, m_TrainCar.transform.TransformPoint(position));
    if (baseEntity == null)
        return null;

    // NPC/Bradley живут отдельно; остальное — родим в вагон ДО Spawn()
    bool shouldParent = !(baseEntity is global::HumanNPC) && !(baseEntity is BradleyAPC);
    if (shouldParent)
    {
        baseEntity.SetParent(m_TrainCar, true, true);
        baseEntity.transform.localPosition = position;
        baseEntity.transform.localRotation = rotation;
    }

    // толстая защита в редакторе (чтобы ничто не ломалось)
    if (baseEntity is BaseCombatEntity be)
        be.baseProtection = _fullProtection;

    // опционально: таймер взлома для hack-крейта (если используешь)
    if (prefab == m_Plugin.HackableCratePrefab && hackTimer > 0)
    {
        var crate = baseEntity as HackableLockedCrate;
        if (crate != null)
        {
            // поставь свою логику таймера, если нужна
        }
    }

    baseEntity.Spawn();

    // маркер типа NPC (для сохранения в layout)
    if (!string.IsNullOrEmpty(npcType) && baseEntity is global::HumanNPC)
        baseEntity.gameObject.AddComponent<NPCTypeMarker>().npcType = npcType;

    // маркер параметров турели (оружие/патроны)
    if (!string.IsNullOrEmpty(gun) && baseEntity is AutoTurret)
        baseEntity.gameObject.AddComponent<TurretMarker>().Set(gun, ammo, ammoCount);

    // безопасная «заморозка» боевого поведения в редакторе
    if (baseEntity is AutoTurret at)
    {
        at.SetFlag(IOEntity.Flag_HasPower, false, false, true);
        at.SetFlag(BaseEntity.Flags.On, false, false, true);
        at.SetTarget(null);
        at.CancelInvoke(at.ServerTick);
        at.CancelInvoke(at.SendAimDir);
        at.CancelInvoke(at.ScheduleForTargetScan);
        at.SendNetworkUpdate();
    }
    else if (baseEntity is SamSite sam)
    {
        samRT.SetFlag(IOEntity.Flag_HasPower, false, false, true);
        sam.SetFlag(BaseEntity.Flags.On, false, false, true);
        sam.CancelInvoke(sam.TargetScan);
        sam.SendNetworkUpdate();
    }
    else if (baseEntity is ScientistNPC npc)
    {
        // «заморозить» ИИ NPC в редакторе
        var brain = npc.GetComponent<BaseAIBrain>();
        if (brain != null) brain.enabled = false;

        var nav = npc.GetComponent<BaseNavigator>();
        if (nav != null)
        {
            nav.SetDestination(npc.transform.position, BaseNavigator.NavigationSpeed.Slow, 0f);
            nav.CanUseNavMesh = false;
            nav.ClearFacingDirectionOverride();
        }
        npc.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);
        npc.SendNetworkUpdate();
    }

    return baseEntity;
}

public BaseEntity CreateChildEntity(string prefab, Vector3 localPos, Quaternion localRot)
{
    return CreateChildEntity(prefab, localPos, localRot, null, null, null, 0, 0f);
}





    private void UpdateNetworkTransform()
    {
        if (m_CurrentEntity == null || m_CurrentEntity.IsDestroyed)
            return;
        
        var rb = m_CurrentEntity.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic)
        {
            rb.position = m_CurrentEntity.transform.position;
            rb.rotation = m_CurrentEntity.transform.rotation;
            rb.MovePosition(m_CurrentEntity.transform.position);
        }
        
        m_CurrentEntity.transform.hasChanged = true;
        m_CurrentEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
    }

    private void UpdatePlacement(ref Construction.Target constructionTarget)
    {
        Vector3 position = m_CurrentEntity.transform.position;
        Quaternion rotation = m_CurrentEntity.transform.rotation;

        Vector3 direction = constructionTarget.ray.direction;
        direction.y = 0f;
        direction.Normalize();

        m_CurrentEntity.transform.position = constructionTarget.ray.origin + (constructionTarget.ray.direction * m_Construction.maxplaceDistance);

        Vector3 eulerRotation = constructionTarget.rotation + m_RotationOffset;
        m_CurrentEntity.transform.rotation = Quaternion.Euler(eulerRotation) * Quaternion.LookRotation(direction);

        m_CurrentEntity.transform.position = Vector3.Lerp(position, m_CurrentEntity.transform.position, Time.deltaTime * 6f);
        m_CurrentEntity.transform.rotation = Quaternion.Lerp(rotation, m_CurrentEntity.transform.rotation, Time.deltaTime * 10f);
    }



}


#endregion

#region HT.HELPERS

private static BaseNetworkable FindBN(ulong id)
{
    return BaseNetworkable.serverEntities.Find(new NetworkableId((uint)id));
}



private void RestoreProtectionForAll()
{
    try
    {
        foreach (var kv in _savedProtection)
        {
            var bn = BaseNetworkable.serverEntities.Find(new NetworkableId(kv.Key));
            var car = bn as TrainCar;
            if (car != null && !car.IsDestroyed)
            {
                car.baseProtection = kv.Value;
                car.SendNetworkUpdate();
            }
        }
    }
    finally
    {
        _savedProtection.Clear();
    }
}

private float ResolveCrateTimerSeconds(string faction, float overrideSeconds)
{
    if (overrideSeconds > 0f) return overrideSeconds;
    var key = (faction ?? "BANDIT").ToUpper();
    if (!config.LootTimerRanges.TryGetValue(key, out var r)) r = new ConfigData.LootTimerRange { Min = 250, Max = 500 };
    return UnityEngine.Random.Range(r.Min, r.Max + 1);
}



public string GetObjectType(BaseEntity entity)
{
    // NPC
    if (entity is ScientistNPC) return "npc";
    if (entity is global::HumanNPC) return "npc";
    if (entity.ShortPrefabName?.Contains("scientist", StringComparison.OrdinalIgnoreCase) == true)
        return "npc";

    // Турели
    if (entity is AutoTurret) return "turret";
    if (entity is SamSite)   return "samsite";

    // Лут (обычные ящики + hackable)
    var prefab = entity?.PrefabName ?? string.Empty;
    if (entity is StorageContainer && (
        prefab.Equals("assets/bundled/prefabs/radtown/crate_elite.prefab", StringComparison.OrdinalIgnoreCase) ||
        prefab.Equals("assets/bundled/prefabs/radtown/crate_normal.prefab", StringComparison.OrdinalIgnoreCase) ||
        prefab.Equals("assets/bundled/prefabs/radtown/crate_normal_2.prefab", StringComparison.OrdinalIgnoreCase)
    )) return "loot";
    if (entity is HackableLockedCrate) return "loot";

    return "unknown";
}

public string GetPrefabByType(string type)
{
    switch (type?.ToLower())
    {
        case "npc":     return SCIENTIST_PREFAB;
        case "turret":  return TURRET_PREFAB;
        case "samsite": return SAMSITE_PREFAB;
        case "loot":    return PREFAB_CRATE_BANDIT; // обычный ящик под фракцию через GetCratePrefabForFaction
        default:        return null;
    }
}


private string GetMinutesWord(int minutes)
{
    if (minutes == 1) return "минуту";
    if (minutes >= 2 && minutes <= 4) return "минуты";
    return "минут";
}

#endregion

[ConsoleCommand("helltrainclean")]
private void CmdClean(ConsoleSystem.Arg arg)
{
    var who = arg?.Player() != null ? arg.Player().displayName : "CONSOLE";
    Puts($"[Helltrain] 🔧 Форс-очистка поезда запрошена: {who}");

    ForceDestroyHellTrainHard();        // 1-й проход
    timer.Once(0.5f, ForceDestroyHellTrainHard); // повтор через полсек
    timer.Once(2.0f, ForceDestroyHellTrainHard); // и контроль через 2с
    arg?.ReplyWith("[Helltrain] Форс-очистка запущена (0.0s/0.5s/2.0s)");
}


[ConsoleCommand("helltrain.fixlayouts")]
private void CmdFixLayouts(ConsoleSystem.Arg arg)
{
    BasePlayer player = arg.Player();
    if (player != null && !player.IsAdmin)
    {
        SendReply(arg, "⛔ Только для админов!");
        return;
    }
    
    var dir = Path.Combine(Interface.Oxide.DataDirectory, LayoutDir);
    if (!Directory.Exists(dir))
    {
        SendReply(arg, "⛔ Папка лэйаутов не найдена!");
        return;
    }
    
    int fixedCount = 0;
    
    foreach (var file in Directory.GetFiles(dir, "*.json"))
    {
        try
        {
            string json = File.ReadAllText(file, System.Text.Encoding.UTF8);
            
            json = json.Replace("\"Name\":", "\"name\":");
            json = json.Replace("\"Faction\":", "\"faction\":");
            json = json.Replace("\"Wagons\":", "\"cars\":");
            json = json.Replace("\"Type\":", "\"type\":");
            json = json.Replace("\"Prefab\":", "\"variant\":");
            
            File.WriteAllText(file, json, System.Text.Encoding.UTF8);
            fixedCount++;
        }
        catch (System.Exception e)
        {
            PrintError($"Ошибка фикса {Path.GetFileName(file)}: {e.Message}");
        }
    }
    
    SendReply(arg, $"✅ Исправлено файлов: {fixedCount}");
    
    _layouts.Clear();
    LoadLayouts();
    
    SendReply(arg, $"✅ Лэйауты перезагружены! Найдено: {_layouts.Count}");
}
} // ← Закрывает класс Helltrain 
}