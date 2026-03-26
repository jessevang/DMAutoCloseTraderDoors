using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]
public class DMCloseDoors_ModApi : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        Debug.Log("[DMCloseDoors] InitMod");

        DMCloseDoors.LoadConfig(_modInstance);

        var harmony = new Harmony("DMCloseDoors");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}

internal static class DMCloseDoors
{
    // Only config we care about
    public static float TraderDoorResetIntervalSeconds = 5f;

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void LoadConfig(Mod modInstance)
    {
        try
        {
            string modPath = GetModPath(modInstance);
            string configPath = Path.Combine(modPath, "Config.xml");

            if (!File.Exists(configPath))
            {
                Debug.Log("[DMCloseDoors] Config.xml not found, using default (5s)");
                ApplyBounds();
                return;
            }

            var doc = new XmlDocument();
            doc.Load(configPath);

            var node = doc.SelectSingleNode("/DMCloseDoors/TraderDoorResetIntervalSeconds");
            if (node?.Attributes?["value"] != null)
            {
                if (float.TryParse(node.Attributes["value"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float val))
                {
                    TraderDoorResetIntervalSeconds = val;
                }
            }

            ApplyBounds();

            Debug.Log($"[DMCloseDoors] Timer set to {TraderDoorResetIntervalSeconds:0.##} seconds");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DMCloseDoors] Failed to load config: " + ex);
            ApplyBounds();
        }
    }

    private static void ApplyBounds()
    {
        if (TraderDoorResetIntervalSeconds < 1f)
            TraderDoorResetIntervalSeconds = 1f;
    }

    private static string GetModPath(Mod modInstance)
    {
        if (modInstance != null)
        {
            var type = modInstance.GetType();

            foreach (var prop in type.GetProperties(AnyInstance))
            {
                if (prop.PropertyType == typeof(string))
                {
                    try
                    {
                        var val = prop.GetValue(modInstance, null) as string;
                        if (!string.IsNullOrEmpty(val))
                            return val;
                    }
                    catch { }
                }
            }
        }

        return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
    }

    public static void ResetTraderDoorsIfOpen(World world)
    {
        if (world == null)
            return;

        foreach (var traderArea in GetTraderAreas(world))
        {
            if (traderArea == null)
                continue;

            try
            {
                if (IsClosed(traderArea))
                    continue;

                var setClosed = GetSetClosed(traderArea.GetType());
                if (setClosed == null)
                    continue;

                var trader = GetTrader(traderArea);

                setClosed.Invoke(traderArea, new object[] { world, true, trader, false });
                setClosed.Invoke(traderArea, new object[] { world, false, trader, false });
            }
            catch { }
        }
    }

    private static bool IsClosed(object ta)
    {
        var t = ta.GetType();

        var prop = t.GetProperty("IsClosed", AnyInstance);
        if (prop != null && prop.PropertyType == typeof(bool))
            return (bool)prop.GetValue(ta, null);

        var field = t.GetField("IsClosed", AnyInstance);
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(ta);

        return false;
    }

    private static IEnumerable<object> GetTraderAreas(World world)
    {
        foreach (var field in world.GetType().GetFields(AnyInstance))
        {
            object val;
            try { val = field.GetValue(world); }
            catch { continue; }

            if (val is IEnumerable en)
            {
                foreach (var item in en)
                {
                    if (item != null && item.GetType().Name == "TraderArea")
                        yield return item;
                }
            }
        }
    }

    private static MethodInfo GetSetClosed(Type t)
    {
        return t.GetMethods(AnyInstance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SetClosed") return false;
                var p = m.GetParameters();
                return p.Length == 4;
            });
    }

    private static object GetTrader(object ta)
    {
        var t = ta.GetType();

        foreach (var prop in t.GetProperties(AnyInstance))
        {
            if (typeof(EntityTrader).IsAssignableFrom(prop.PropertyType))
                return prop.GetValue(ta, null);
        }

        foreach (var field in t.GetFields(AnyInstance))
        {
            if (typeof(EntityTrader).IsAssignableFrom(field.FieldType))
                return field.GetValue(ta);
        }

        return null;
    }
}