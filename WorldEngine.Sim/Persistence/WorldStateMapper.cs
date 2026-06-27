using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Beasts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using WorldEngine.Sim.WorldGen.Layers;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// Maps between WorldState and WorldStateDto.
/// Regenerates TileGrid + SeasonalProfiles from saved seed+dimensions (deterministic).
/// </summary>
internal static class WorldStateMapper
{
    // ── Key helpers ──────────────────────────────────────────────────────────
    private static string TileKey(TileCoord c) => $"{c.X},{c.Y}";
    private static TileCoord ParseTile(string s)
    {
        var i = s.IndexOf(',');
        return new TileCoord(int.Parse(s[..i]), int.Parse(s[(i + 1)..]));
    }

    // ── ToDto ────────────────────────────────────────────────────────────────
    public static WorldStateDto ToDto(WorldState w)
    {
        return new WorldStateDto(
            Seed:                        w.Config.Seed,
            WidthKm:                     w.Config.WidthKm,
            HeightKm:                    w.Config.HeightKm,
            TileWidthKm:                 w.Config.TileWidthKm,
            CurrentYear:                 w.CurrentYear,
            CurrentSeason:               (int)w.CurrentSeason,
            CurrentTick:                 w.CurrentTick,
            GlobalTemperatureAnomaly:    w.GlobalTemperatureAnomaly,
            CurrentSeaLevel:             w.CurrentSeaLevel,
            GlobalPrecipitationMultiplier: w.GlobalPrecipitationMultiplier,
            StormCorridorNormalizedLat:  w.StormCorridorNormalizedLat,
            StormCorridorHalfWidth:      w.StormCorridorHalfWidth,
            MonsoonIntensityMultiplier:  w.MonsoonIntensityMultiplier,
            VolcanicActivityMultiplier:  w.VolcanicActivityMultiplier,
            NextCivId:                   w.NextCivId,
            ResourceRegistry:            MapResourceRegistry(w),
            ActiveTileDisasters:         MapActiveTileDisasters(w),
            ActiveDroughts:              MapActiveDroughts(w),
            Civilizations:               MapCivilizations(w),
            Settlements:                 MapSettlements(w),
            Ruins:                       MapRuins(w),
            TerritoryMap:                MapTerritoryMap(w),
            ImprovementMap:              MapImprovementMap(w),
            Entities:                    MapEntities(w),
            Relationships:               MapRelationships(w),
            NameOrdinals:                new Dictionary<string, int>(w.NameOrdinals, StringComparer.OrdinalIgnoreCase),
            ActiveFounders:              w.ActiveFounders.Select(e => e.Value).ToList(),
            BeastEmergenceSchedule:      w.BeastEmergenceSchedule
                                          .Select(e => new BeastEmergenceEntryDto(e.EmergenceYear, e.SpeciesId))
                                          .ToList(),
            WatchedCharacterId:          w.WatchedCharacterId?.Value,
            PendingEmissaries:           w.PendingEmissaries
                                          .Select(e => new PendingEmissaryDto(
                                              e.FromCiv.Value, e.ToCiv.Value,
                                              (int)e.Purpose, e.DepartedYear, e.ArrivalYear, e.SurvivalChance))
                                          .ToList());
    }

    private static Dictionary<string, List<ResourceDepositDto>> MapResourceRegistry(WorldState w)
    {
        var result = new Dictionary<string, List<ResourceDepositDto>>(w.ResourceRegistry.Count);
        foreach (var (tile, deposits) in w.ResourceRegistry)
            result[TileKey(tile)] = deposits.Select(d => new ResourceDepositDto(d.DepositType, d.Quality, d.Depth)).ToList();
        return result;
    }

    private static Dictionary<string, List<ActiveDisasterDto>> MapActiveTileDisasters(WorldState w)
    {
        var result = new Dictionary<string, List<ActiveDisasterDto>>(w.ActiveTileDisasters.Count);
        foreach (var (tile, disasters) in w.ActiveTileDisasters)
            result[TileKey(tile)] = disasters.Select(d => new ActiveDisasterDto(
                (int)d.Type, d.Intensity, d.TicksRemaining, d.OriginEventId.Value)).ToList();
        return result;
    }

    private static List<ActiveDroughtDto> MapActiveDroughts(WorldState w) =>
        w.ActiveDroughts.Select(d => new ActiveDroughtDto(
            d.LatitudeBandIndex, (int)d.AffectedBiome, d.Intensity, d.SeasonsRemaining, d.OriginEventId.Value)).ToList();

    private static List<CivilizationDto> MapCivilizations(WorldState w) =>
        w.Civilizations.Values.Select(MapCiv).ToList();

    private static CivilizationDto MapCiv(Civilization c)
    {
        var cityTerr = new Dictionary<string, List<string>>(c.CityTerritories.Count);
        foreach (var (city, tiles) in c.CityTerritories)
            cityTerr[TileKey(city)] = tiles.Select(TileKey).ToList();

        var knownCivs = new Dictionary<string, CivContactDto>(c.KnownCivs.Count);
        foreach (var (targetId, contact) in c.KnownCivs)
            knownCivs[targetId.Value.ToString()] = new CivContactDto(
                contact.KnownCivId.Value, contact.YearFirstContact, contact.YearLastContact,
                (int)contact.BestSource, TileKey(contact.CapitalTile), contact.Confidence);

        return new CivilizationDto(
            Id:                      c.Id.Value,
            Name:                    c.Name,
            FounderId:               c.FounderId.Value,
            RulerId:                 c.RulerId.Value,
            CapitalTile:             TileKey(c.CapitalTile),
            FoundedYear:             c.FoundedYear,
            IsCollapsed:             c.IsCollapsed,
            CollapseYear:            c.CollapseYear,
            LastSettlementFoundedYear: c.LastSettlementFoundedYear,
            SettlementCount:         c.SettlementCount,
            ColonyCount:             c.ColonyCount,
            TotalPopulation:         c.TotalPopulation,
            SuccessionCrisisEndYear: c.SuccessionCrisisEndYear,
            RulerCount:              c.RulerCount,
            TotalWarsInitiated:      c.TotalWarsInitiated,
            TotalSuccessions:        c.TotalSuccessions,
            TotalSettlementsFounded: c.TotalSettlementsFounded,
            NearCollapseCount:       c.NearCollapseCount,
            TotalScholarDiscoveries: c.TotalScholarDiscoveries,
            Members:                 c.Members.Select(e => e.Value).ToList(),
            BorderTension:           c.BorderTension.ToDictionary(kv => kv.Key.Value.ToString(), kv => kv.Value),
            WarsAgainst:             c.WarsAgainst.ToDictionary(kv => kv.Key.Value.ToString(), kv => kv.Value),
            PeaceTreaties:           c.PeaceTreaties.ToDictionary(kv => kv.Key.Value.ToString(), kv => kv.Value),
            WarHistory:              c.WarHistory.ToDictionary(kv => kv.Key.Value.ToString(), kv => kv.Value),
            CulturalTraits:          c.CulturalTraits.ToList(),
            CityTerritories:         cityTerr,
            CulturalProfile:         c.CulturalProfile is null ? null : new CulturalProfileDto(
                c.CulturalProfile.AncestryId,
                c.CulturalProfile.ArchitecturalStyle,
                c.CulturalProfile.SettlementDescriptor,
                c.CulturalProfile.ArtisticTraditions,
                c.CulturalProfile.ActiveTraits,
                c.CulturalProfile.DominantBiome),
            KnownCivs:               knownCivs,
            ActiveEmissaryCountByTarget: c.ActiveEmissaryCountByTarget
                .ToDictionary(kv => kv.Key.Value.ToString(), kv => kv.Value));
    }

    private static Dictionary<string, SettlementStubDto> MapSettlements(WorldState w)
    {
        var result = new Dictionary<string, SettlementStubDto>(w.Settlements.Count);
        foreach (var (tile, s) in w.Settlements)
            result[TileKey(tile)] = new SettlementStubDto(
                s.FounderId.Value, s.CivId.Value, TileKey(s.Tile), s.FoundedYear,
                s.Population, s.Health, s.Name, s.PopulationF, s.LastCrystalThresh,
                s.FoodPressureRatio, s.WaterPressureRatio, s.LastStrainEventTick,
                s.ResourceLedger is null ? null : new Dictionary<string, float>(s.ResourceLedger),
                s.FertilityMultiplier, s.ConqueredYear, s.ConqueredFromCivId,
                s.ResourceStores is null ? null : new Dictionary<string, float>(s.ResourceStores),
                s.CarryingCapacity, s.IsColony, s.IsInfected, s.InfectedSinceYear);
        return result;
    }

    private static Dictionary<string, RuinRecordDto> MapRuins(WorldState w)
    {
        var result = new Dictionary<string, RuinRecordDto>(w.Ruins.Count);
        foreach (var (tile, r) in w.Ruins)
            result[TileKey(tile)] = new RuinRecordDto(
                TileKey(r.Tile), r.SettlementName, r.OriginalCivId.Value, r.DestroyedYear, r.Cause, r.TimesSettled);
        return result;
    }

    private static Dictionary<string, string> MapTerritoryMap(WorldState w)
    {
        var result = new Dictionary<string, string>(w.TerritoryMap.Count);
        foreach (var (tile, city) in w.TerritoryMap)
            result[TileKey(tile)] = TileKey(city);
        return result;
    }

    private static Dictionary<string, TileImprovementDto> MapImprovementMap(WorldState w)
    {
        var result = new Dictionary<string, TileImprovementDto>(w.ImprovementMap.Count);
        foreach (var (tile, imp) in w.ImprovementMap)
            result[TileKey(tile)] = new TileImprovementDto(
                (int)imp.Type, TileKey(imp.CityTile), imp.BuiltYear, imp.BuilderId.Value);
        return result;
    }

    private static List<EntityDto> MapEntities(WorldState w)
    {
        var result = new List<EntityDto>(w.Entities.Count);
        foreach (var c in w.Entities.Characters)
            result.Add(new EntityDto("tier1", MapTier1(c), null, null));
        foreach (var c in w.Entities.Tier2Chars)
            result.Add(new EntityDto("tier2", null, MapTier2(c), null));
        foreach (var b in w.Entities.Beasts)
            result.Add(new EntityDto("beast", null, null, MapBeast(b)));
        return result;
    }

    private static Tier1EntityDto MapTier1(Tier1Character c)
    {
        var p = c.Personality;
        var a = c.Aptitude;
        var sk = c.Skills;
        var n = c.Needs;
        return new Tier1EntityDto(
            Id: c.Id.Value, LocationX: c.Location.X, LocationY: c.Location.Y,
            IsAlive: c.IsAlive, Health: c.Health, MaxHealth: c.MaxHealth,
            AgeSeason: c.AgeSeason, MaxAgeSeason: c.MaxAgeSeason,
            Personality: [p.Ambition, p.Greed, p.Aggression, p.Compassion, p.Curiosity, p.Creativity, p.Rationality, p.Wonder, p.Loyalty, p.Sociability, p.Honesty, p.Stability],
            Aptitude:    [a.Diligence, a.Focus, a.Perfectionism, a.Composure, a.Acuity, a.Ingenuity],
            Skills:      [sk.Combat, sk.Leadership, sk.Administration, sk.Diplomacy, sk.Crafting, sk.Knowledge, sk.Stealth, sk.Piety],
            Needs:       [n.Safety, n.Food, n.Shelter, n.Belonging, n.Status, n.Purpose, n.Spiritual],
            Identity:    MapIdentity(c.Identity),
            Goals:       c.Goals.Select(MapGoal).ToList(),
            IsInfected:  c.IsInfected, InfectedSinceYear: c.InfectedSinceYear,
            Wellbeing:   c.Wellbeing, TicksInCurrentTile: c.TicksInCurrentTile,
            LastCreateCompletedTick: c.LastCreateCompletedTick);
    }

    private static Tier2EntityDto MapTier2(Tier2Character c)
    {
        var p = c.Personality;
        var n = c.Needs;
        var l = c.Livelihood;
        return new Tier2EntityDto(
            Id: c.Id.Value, LocationX: c.Location.X, LocationY: c.Location.Y,
            IsAlive: c.IsAlive, Health: c.Health, MaxHealth: c.MaxHealth,
            AgeSeason: c.AgeSeason, MaxAgeSeason: c.MaxAgeSeason,
            Name: c.Name,
            Personality6: [p.Ambition, p.Loyalty, p.Diligence, p.Sociability, p.Cunning, p.Rationality],
            Needs4:       [n.Food, n.Safety, n.Belonging, n.Status],
            Livelihood:   new LivelihoodDataDto((int)l.Role, l.EmployerId?.Value, TileKey(l.SettlementTile), l.IncomeLevel),
            LastNotableWorkTick: c.LastNotableWorkTick, HasMasterwork: c.HasMasterwork);
    }

    private static BeastEntityDto MapBeast(LegendaryBeast b) =>
        new(Id: b.Id.Value, LocationX: b.Location.X, LocationY: b.Location.Y,
            HomeTileX: b.HomeTile.X, HomeTileY: b.HomeTile.Y,
            IsAlive: b.IsAlive, Health: b.Health, MaxHealth: b.MaxHealth,
            AgeSeason: b.AgeSeason, MaxAgeSeason: b.MaxAgeSeason,
            SpeciesId: b.SpeciesId, Name: b.Name, IsLegendary: b.IsLegendary,
            Strength: b.Strength, Speed: b.Speed, Aggression: b.Aggression,
            TerritoryRadius: b.TerritoryRadius, Abilities: b.Abilities,
            FoodNeed: b.FoodNeed, SafetyNeed: b.SafetyNeed,
            FoodDepletion: b.FoodDepletion, FoodFromHunt: b.FoodFromHunt,
            FoodFromGraze: b.FoodFromGraze, ReproductionChance: b.ReproductionChance,
            ReproductionMinAge: b.ReproductionMinAge,
            ReproductionFoodThreshold: b.ReproductionFoodThreshold,
            Hibernates: b.Hibernates, PrefersCompany: b.PrefersCompany);

    private static IdentityDataDto MapIdentity(IdentityData id) =>
        new(id.Name, id.Epithet, id.AncestryId,
            id.MotherId?.Value, id.FatherId?.Value,
            id.CivId.Value, id.BirthYear, id.BirthSeason, id.NameOrdinal, id.RulerOrdinal);

    private static GoalDataDto MapGoal(GoalData g) =>
        new((int)g.Type, (int)g.Object,
            g.TargetEntityId?.Value,
            g.TargetTile.HasValue ? TileKey(g.TargetTile.Value) : null,
            g.Priority, g.Progress, g.IsComplete, g.StaleSince,
            g.Intensity, g.FormedTick, g.ResourceTag);

    private static List<RelationshipEdgeDto> MapRelationships(WorldState w) =>
        w.Relationships.AllEdges.Select(e => new RelationshipEdgeDto(
            e.From.Value, e.To.Value, e.Trust, e.Fear, e.Debt, (int)e.Flags)).ToList();

    // ── FromDto ──────────────────────────────────────────────────────────────
    public static WorldState FromDto(WorldStateDto dto, SimConfig cfg)
    {
        // 1. Reconstruct WorldConfig
        var worldCfg = new WorldConfig
        {
            Seed       = dto.Seed,
            WidthKm    = dto.WidthKm,
            HeightKm   = dto.HeightKm,
            TileWidthKm = dto.TileWidthKm
        };

        // 2. Regenerate TileGrid + SeasonalProfiles from seed (deterministic)
        var ctx = new WorldGenContext(worldCfg, cfg);
        ctx.Tectonic  = new TectonicLayer().Generate(ctx);
        ctx.Elevation = new ElevationLayer().Generate(ctx);
        ctx.Ocean     = new OceanLayer().Generate(ctx);
        ctx.River     = new RiverLayer().Generate(ctx);
        ctx.Magic     = new MagicLayer().Generate(ctx);
        ctx.Climate   = new ClimateLayer().Generate(ctx);
        ctx.Biome     = new BiomeLayer().Generate(ctx);
        ctx.Resource  = new ResourceLayer().Generate(ctx);
        ctx.Poi       = new PoiCandidateLayer().Generate(ctx);

        // 3. Restore resource registry (override the worldgen-generated one)
        var resourceRegistry = RestoreResourceRegistry(dto);

        // 4. Build the partial WorldState from worldgen output, then swap resource registry
        //    We need TileGrid + SeasonalProfiles from ctx; assemble via a temp WorldState
        var assembled = TileGridAssembler.Assemble(ctx);

        // 5. Create WorldState with saved resource registry
        var world = new WorldState(worldCfg, cfg, assembled.TileGrid, assembled.SeasonalProfiles,
            resourceRegistry, dto.StormCorridorNormalizedLat);

        // 6. Restore time
        world.CurrentYear   = dto.CurrentYear;
        world.CurrentSeason = (Season)dto.CurrentSeason;
        world.CurrentTick   = dto.CurrentTick;

        // 7. Restore environmental drift
        world.GlobalTemperatureAnomaly     = dto.GlobalTemperatureAnomaly;
        world.CurrentSeaLevel              = dto.CurrentSeaLevel;
        world.GlobalPrecipitationMultiplier = dto.GlobalPrecipitationMultiplier;
        world.StormCorridorHalfWidth       = dto.StormCorridorHalfWidth;
        world.MonsoonIntensityMultiplier   = dto.MonsoonIntensityMultiplier;
        world.VolcanicActivityMultiplier   = dto.VolcanicActivityMultiplier;

        // 8. Restore civ counter
        world.NextCivId = dto.NextCivId;

        // 9. Restore disasters
        foreach (var (key, disasters) in dto.ActiveTileDisasters)
        {
            var tile = ParseTile(key);
            world.ActiveTileDisasters[tile] = disasters.Select(d => new ActiveDisaster(
                (DisasterType)d.DisasterType, d.Intensity, d.TicksRemaining,
                new EventId(d.OriginEventId))).ToList();
        }
        foreach (var d in dto.ActiveDroughts)
            world.ActiveDroughts.Add(new ActiveDrought(
                d.LatitudeBandIndex, (BiomeType)d.AffectedBiome, d.Intensity,
                d.SeasonsRemaining, new EventId(d.OriginEventId)));

        // 10. Restore civilizations
        foreach (var cdto in dto.Civilizations)
        {
            var civ = RestoreCivilization(cdto);
            world.Civilizations[civ.Id] = civ;
        }

        // 11. Restore settlements
        foreach (var (key, s) in dto.Settlements)
        {
            var tile = ParseTile(key);
            var stub = new SettlementStub(
                new EntityId(s.FounderId), new CivId(s.CivId), ParseTile(s.Tile),
                s.FoundedYear, s.Population, s.Health, s.Name, s.PopulationF,
                s.LastCrystalThresh, s.FoodPressureRatio, s.WaterPressureRatio,
                s.LastStrainEventTick,
                s.ResourceLedger is null ? null : (IReadOnlyDictionary<string, float>)s.ResourceLedger,
                s.FertilityMultiplier, s.ConqueredYear, s.ConqueredFromCivId,
                s.ResourceStores is null ? null : (IReadOnlyDictionary<string, float>)s.ResourceStores,
                s.CarryingCapacity, s.IsColony, s.IsInfected, s.InfectedSinceYear);
            world.Settlements[tile] = stub;
        }

        // 12. Restore ruins
        foreach (var (key, r) in dto.Ruins)
        {
            var tile = ParseTile(key);
            world.Ruins[tile] = new RuinRecord(
                ParseTile(r.Tile), r.SettlementName, new CivId(r.OriginalCivId),
                r.DestroyedYear, r.Cause, r.TimesSettled);
        }

        // 13. Restore territory map
        foreach (var (key, val) in dto.TerritoryMap)
            world.TerritoryMap[ParseTile(key)] = ParseTile(val);

        // 14. Restore improvement map
        foreach (var (key, imp) in dto.ImprovementMap)
        {
            world.ImprovementMap[ParseTile(key)] = new TileImprovement(
                (ImprovementType)imp.ImprovementType, ParseTile(imp.CityTile),
                imp.BuiltYear, new EntityId(imp.BuilderId));
        }

        // 15. Restore entities
        long maxEntityId = 0;
        foreach (var edto in dto.Entities)
        {
            IEntity entity = edto.Kind switch
            {
                "tier1" => RestoreTier1(edto.Tier1!),
                "tier2" => RestoreTier2(edto.Tier2!),
                "beast" => RestoreBeast(edto.Beast!),
                _       => throw new InvalidOperationException($"Unknown entity kind: {edto.Kind}")
            };
            world.Entities.Add(entity);
            maxEntityId = Math.Max(maxEntityId, entity.Id.Value);
        }

        // Advance entity ID counter past all loaded IDs to prevent collisions
        if (maxEntityId > 0)
            EntityId.EnsureCounterExceeds(maxEntityId);

        // 16. Restore relationships
        foreach (var r in dto.Relationships)
            world.Relationships.Upsert(new RelationshipEdge(
                new EntityId(r.From), new EntityId(r.To),
                r.Trust, r.Fear, r.Debt, (RelationshipFlags)r.Flags));

        // 17. Restore name ordinals
        foreach (var kv in dto.NameOrdinals)
            world.NameOrdinals[kv.Key] = kv.Value;

        // 18. Restore active founders
        foreach (var id in dto.ActiveFounders)
            world.AddActiveFounder(new EntityId(id));

        // 19. Restore beast emergence schedule
        foreach (var e in dto.BeastEmergenceSchedule)
            world.BeastEmergenceSchedule.Add((e.EmergenceYear, e.SpeciesId));

        // 20. Restore watched character
        if (dto.WatchedCharacterId.HasValue)
            world.WatchedCharacterId = new EntityId(dto.WatchedCharacterId.Value);

        // 21. Restore pending emissaries (M4 Phase 1)
        foreach (var e in dto.PendingEmissaries)
            world.PendingEmissaries.Add(new Civilizations.PendingEmissary(
                new CivId(e.FromCiv), new CivId(e.ToCiv),
                (Civilizations.EmissaryPurpose)e.Purpose,
                e.DepartedYear, e.ArrivalYear, e.SurvivalChance));

        return world;
    }

    private static Dictionary<TileCoord, List<ResourceDeposit>> RestoreResourceRegistry(WorldStateDto dto)
    {
        var result = new Dictionary<TileCoord, List<ResourceDeposit>>(dto.ResourceRegistry.Count);
        foreach (var (key, deposits) in dto.ResourceRegistry)
            result[ParseTile(key)] = deposits.Select(d => new ResourceDeposit(d.DepositType, d.Quality, d.Depth)).ToList();
        return result;
    }

    private static Civilization RestoreCivilization(CivilizationDto dto)
    {
        var civ = new Civilization(
            new CivId(dto.Id), dto.Name,
            new EntityId(dto.FounderId), ParseTile(dto.CapitalTile), dto.FoundedYear);

        civ.RulerId                  = new EntityId(dto.RulerId);
        civ.IsCollapsed              = dto.IsCollapsed;
        civ.CollapseYear             = dto.CollapseYear;
        civ.LastSettlementFoundedYear = dto.LastSettlementFoundedYear;
        civ.SettlementCount          = dto.SettlementCount;
        civ.ColonyCount              = dto.ColonyCount;
        civ.TotalPopulation          = dto.TotalPopulation;
        civ.SuccessionCrisisEndYear  = dto.SuccessionCrisisEndYear;
        civ.RulerCount               = dto.RulerCount;
        civ.TotalWarsInitiated       = dto.TotalWarsInitiated;
        civ.TotalSuccessions         = dto.TotalSuccessions;
        civ.TotalSettlementsFounded  = dto.TotalSettlementsFounded;
        civ.NearCollapseCount        = dto.NearCollapseCount;
        civ.TotalScholarDiscoveries  = dto.TotalScholarDiscoveries;

        foreach (var id in dto.Members) civ.Members.Add(new EntityId(id));
        foreach (var kv in dto.BorderTension) civ.BorderTension[new CivId(int.Parse(kv.Key))] = kv.Value;
        foreach (var kv in dto.WarsAgainst)   civ.WarsAgainst[new CivId(int.Parse(kv.Key))]   = kv.Value;
        foreach (var kv in dto.PeaceTreaties) civ.PeaceTreaties[new CivId(int.Parse(kv.Key))] = kv.Value;
        foreach (var kv in dto.WarHistory)    civ.WarHistory[new CivId(int.Parse(kv.Key))]     = kv.Value;
        foreach (var t in dto.CulturalTraits) civ.CulturalTraits.Add(t);

        foreach (var (cityKey, tilePaths) in dto.CityTerritories)
        {
            var cityTile = ParseTile(cityKey);
            var tileSet  = tilePaths.Select(ParseTile).ToHashSet();
            civ.CityTerritories[cityTile] = tileSet;
        }

        if (dto.CulturalProfile is { } cp)
            civ.CulturalProfile = new CulturalProfile(
                cp.AncestryId, cp.ArchitecturalStyle, cp.SettlementDescriptor,
                cp.ArtisticTraditions, cp.ActiveTraits, cp.DominantBiome);

        // M4 Phase 1 — restore civ awareness data
        foreach (var (key, cd) in dto.KnownCivs)
        {
            var knownId = new CivId(int.Parse(key));
            civ.KnownCivs[knownId] = new Civilizations.CivContact(
                knownId, cd.YearFirstContact, cd.YearLastContact,
                (Civilizations.CivContactSource)cd.BestSource,
                ParseTile(cd.CapitalTile), cd.Confidence);
        }
        foreach (var kv in dto.ActiveEmissaryCountByTarget)
            civ.ActiveEmissaryCountByTarget[new CivId(int.Parse(kv.Key))] = kv.Value;

        return civ;
    }

    private static Tier1Character RestoreTier1(Tier1EntityDto d)
    {
        var p  = d.Personality;
        var a  = d.Aptitude;
        var sk = d.Skills;

        var personality = new PersonalityVector(
            p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9], p[10], p[11]);
        var aptitude = new AptitudeVector(a[0], a[1], a[2], a[3], a[4], a[5]);
        var skills   = new SkillVector(sk[0], sk[1], sk[2], sk[3], sk[4], sk[5], sk[6], sk[7]);
        var id       = d.Identity;
        var identity = new IdentityData(
            id.Name, id.Epithet, id.AncestryId,
            id.MotherId.HasValue ? new EntityId(id.MotherId.Value) : null,
            id.FatherId.HasValue ? new EntityId(id.FatherId.Value) : null,
            new CivId(id.CivId), id.BirthYear, id.BirthSeason, id.NameOrdinal, id.RulerOrdinal);

        var character = new Tier1Character(
            new EntityId(d.Id), new TileCoord(d.LocationX, d.LocationY),
            personality, aptitude, skills, identity, d.MaxHealth, d.MaxAgeSeason);

        character.IsAlive             = d.IsAlive;
        character.Health              = d.Health;
        character.AgeSeason           = d.AgeSeason;
        character.Needs               = new NeedsVector(d.Needs[0], d.Needs[1], d.Needs[2], d.Needs[3], d.Needs[4], d.Needs[5], d.Needs[6]);
        character.IsInfected          = d.IsInfected;
        character.InfectedSinceYear   = d.InfectedSinceYear;
        character.Wellbeing           = d.Wellbeing;
        character.TicksInCurrentTile  = d.TicksInCurrentTile;
        character.LastCreateCompletedTick = d.LastCreateCompletedTick;

        foreach (var gd in d.Goals)
            character.Goals.Add(new GoalData
            {
                Type           = (GoalType)gd.Type,
                Object         = (GoalObject)gd.Object,
                TargetEntityId = gd.TargetEntityId.HasValue ? new EntityId(gd.TargetEntityId.Value) : null,
                TargetTile     = gd.TargetTile is not null ? ParseTile(gd.TargetTile) : null,
                Priority       = gd.Priority,
                Progress       = gd.Progress,
                IsComplete     = gd.IsComplete,
                StaleSince     = gd.StaleSince,
                Intensity      = gd.Intensity,
                FormedTick     = gd.FormedTick,
                ResourceTag    = gd.ResourceTag
            });

        return character;
    }

    private static Tier2Character RestoreTier2(Tier2EntityDto d)
    {
        var p = d.Personality6;
        var personality = new PersonalityVector6(p[0], p[1], p[2], p[3], p[4], p[5]);
        var l = d.Livelihood;
        var livelihood = new LivelihoodData(
            (Tier2Role)l.Role,
            l.EmployerId.HasValue ? new EntityId(l.EmployerId.Value) : null,
            ParseTile(l.SettlementTile),
            l.IncomeLevel);

        var character = new Tier2Character(
            new EntityId(d.Id), new TileCoord(d.LocationX, d.LocationY),
            d.Name, personality, livelihood, d.MaxHealth, d.MaxAgeSeason);

        character.IsAlive             = d.IsAlive;
        character.Health              = d.Health;
        character.AgeSeason           = d.AgeSeason;
        var n = d.Needs4;
        character.Needs               = new NeedsVector4(n[0], n[1], n[2], n[3]);
        character.LastNotableWorkTick = d.LastNotableWorkTick;
        character.HasMasterwork       = d.HasMasterwork;

        return character;
    }

    private static LegendaryBeast RestoreBeast(BeastEntityDto d)
    {
        // Create with HomeTile as the constructor's location parameter (sets HomeTile correctly)
        var beast = new LegendaryBeast(
            new EntityId(d.Id), d.SpeciesId, d.Name,
            new TileCoord(d.HomeTileX, d.HomeTileY),
            d.IsLegendary, d.MaxHealth, d.Strength, d.Speed, d.Aggression,
            d.TerritoryRadius, d.Abilities, d.MaxAgeSeason,
            d.FoodDepletion, d.FoodFromHunt, d.FoodFromGraze,
            d.ReproductionChance, d.ReproductionMinAge, d.ReproductionFoodThreshold,
            d.Hibernates, d.PrefersCompany);

        // Restore actual current location (may differ from HomeTile if beast moved)
        beast.Location   = new TileCoord(d.LocationX, d.LocationY);
        beast.IsAlive    = d.IsAlive;
        beast.Health     = d.Health;
        beast.AgeSeason  = d.AgeSeason;
        beast.FoodNeed   = d.FoodNeed;
        beast.SafetyNeed = d.SafetyNeed;

        return beast;
    }
}
