using System;
using System.Collections.Generic;
using UnityEngine;

namespace FieldShift
{
    // All game content lives in Resources/FieldShift/catalog.json so designers (and the Nations reviewing
    // the Indigenous content) can change text and numbers without touching code.

    [Serializable]
    public class SoilInfo
    {
        public string name;
        public float tawMm = 150;          // total available water in the root zone, mm
        public float drainMmPerDay = 20;   // how fast saturated soil drains
        public float organicMatter = 3;    // %
        public float nitrogen = 40;        // plant-available N, kg/ha
        public float phosphorus = 70;      // plant-available P, kg P2O5/ha
        public float potassium = 200;      // plant-available K, kg K2O/ha
        public string source;
    }

    [Serializable]
    public class LocationDef
    {
        public string id, name, region, blurb, territory, nation, acknowledgement;
        public float lat, lon;
        public SoilInfo soil;
        public float startMoney = 30000, creditLimit = 25000, leasePerHa = 800, livingCostPerDay = 35;
        public bool irrigationAvailable = true;
        public float irrigationAllocationMm = 300;
        public bool floodProne, dryland, riverRestrictions;
        public string[] crops = new string[0];
        public string[] covers = new string[0];
        public string[] techniques = new string[0];
        public string existingMain, existingCover, existingLabel;
        public float treeDensity = 0.7f, dryGrass;
        public string label = "right";        // where the name sits next to its pin on the BC map
        public bool wetClimate;               // IPCC wet/dry climate class for nitrous oxide factors
        public string existingFert = "synthetic";
    }

    [Serializable]
    public class CropDef
    {
        public string id, name, kind, family, description;
        public float kcMid = 1, heatC = 35, frostC = 0, droughtSens = 1, floodTolDays = 2;
        public float baseTempC = 5, gddToMaturity = 1000, regrowGdd;
        public int plantFrom, plantTo, lastHarvestDoy;
        public float yieldPerHa, pricePerUnit, costPerHa, harvestCostPerHa;
        public string unit;
        public float nDemand, nFix, omPerYear, erosionCover = 0.5f, height = 1;
        public bool perennial, overwinters, scavengesN, noIrrigation, needsLowGround, storesFloodwater, sheltersNeighbours, reducesNeighbourPests;
        public float pRemoval = 3, kRemoval = 4;            // kg P2O5 and K2O removed per unit of harvest
        public float dieselPlant = 20, dieselHarvest = 15;  // litres per hectare per operation
        public float annualCostPerHa;                       // yearly upkeep of perennial crops
        public int yearsToFullYield = 1;                    // perennials like blueberries take years to come into full production
        public Color color = Color.green;

        public bool IsCash => kind == "cash";
        public bool IsCover => kind == "cover";
        public bool IsIndigenous => kind == "indigenous";
        public float GrossPerHa => yieldPerHa * pricePerUnit;
    }

    [Serializable]
    public class FertilizerDef
    {
        public string id, name, desc;
        public float n, p, k;              // kg N, P2O5, K2O per tonne of product
        public float nAvail = 1;           // share of N available in the first season
        public float pricePerT, omPerT;    // $ per tonne, organic matter % added per tonne per ha
        public bool mix;                   // synthetic: urea + MAP + potash mixed to the crop's needs
    }

    [Serializable]
    public class EmissionFactors
    {
        public float gwpN2O = 273, gwpCH4 = 27;
        public float ef1SyntheticWet = 0.016f, ef1OrganicWet = 0.006f, ef1Dry = 0.005f;
        public float fracGasSynthetic = 0.11f, fracGasOrganic = 0.21f, ef4Wet = 0.014f, ef4Dry = 0.005f, ef5Leach = 0.011f;
        public float makeN = 4f, makeP = 1.2f, makeK = 0.6f, ureaCO2PerKgN = 1.59f;
        public float dieselKgCO2PerL = 2.7f, pumpKWhPerMmHa = 4f, gridKgCO2PerKWh = 0.03f;
        public float ch4KgPerWetDay = 0.5f, soilCO2PerPercentOM = 6.5f;
        public string source;
        public const float N2OPerN = 44f / 28f;
    }

    [Serializable]
    public class TechniqueDef
    {
        public string id, location, nation, title, teaching, inGame, source, review;
    }

    [Serializable]
    public class StoryBeat
    {
        public string id, title, text, more;   // text is short; more is the longer version behind "Learn more"
    }

    [Serializable]
    public class CatalogRoot
    {
        public LocationDef[] locations;
        public CropDef[] crops;
        public TechniqueDef[] techniques;
        public StoryBeat[] story;
        public string[] sources;
        public FertilizerDef[] fertilizers = new FertilizerDef[0];
        public EmissionFactors emissionFactors = new EmissionFactors();
    }

    public class Catalog
    {
        public CatalogRoot root;
        readonly Dictionary<string, CropDef> crops = new Dictionary<string, CropDef>();
        readonly Dictionary<string, StoryBeat> story = new Dictionary<string, StoryBeat>();

        static Catalog instance;
        public static Catalog Instance => instance ??= Load();

        public static Catalog Load()
        {
            var ta = Resources.Load<TextAsset>("FieldShift/catalog");
            if (ta == null) throw new Exception("Missing Resources/FieldShift/catalog.json");
            var c = new Catalog { root = JsonUtility.FromJson<CatalogRoot>(ta.text) };
            foreach (var cd in c.root.crops) c.crops[cd.id] = cd;
            foreach (var s in c.root.story) c.story[s.id] = s;
            return c;
        }

        public CropDef Crop(string id) => !string.IsNullOrEmpty(id) && crops.TryGetValue(id, out var c) ? c : null;
        public StoryBeat Story(string id) => story.TryGetValue(id, out var s) ? s : null;
        public LocationDef Location(string id) => Array.Find(root.locations, l => l.id == id);
        public TechniqueDef Technique(string id) => Array.Find(root.techniques, t => t.id == id);
        public FertilizerDef Fertilizer(string id) => Array.Find(root.fertilizers, f => f.id == id) ?? Array.Find(root.fertilizers, f => f.id == "none");
        public EmissionFactors EF => root.emissionFactors ?? new EmissionFactors();
    }
}
