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

        DMCloseDoors.LoadConfig();

        var harmony = new Harmony("DMCloseDoors");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        Debug.Log("[DMCloseDoors] Harmony patches applied");
    }
}

internal static class DMCloseDoors
{
    public static bool EnableInTraders = true;
    public static bool DebugLog = false;

    public static float ResetIntervalSeconds = 10f;

    private static readonly BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void LoadConfig()
    {
        try
        {
            string modFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(modFolder, "config.xml");

            if (!File.Exists(configPath))
            {
                Debug.Log("[DMCloseDoors] config.xml not found. Using default ResetIntervalSeconds=10");
                return;
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(configPath);

            XmlNode node = doc.SelectSingleNode("/Config/TraderDoorResetIntervalSeconds");
            if (node != null)
            {
                float parsedValue;
                if (float.TryParse(node.InnerText, out parsedValue) && parsedValue > 0f)
                {
                    ResetIntervalSeconds = parsedValue;
                }
                else
                {
                    Debug.LogWarning("[DMCloseDoors] Invalid TraderDoorResetIntervalSeconds in config.xml. Using default ResetIntervalSeconds=10");
                }
            }
            else
            {
                Debug.Log("[DMCloseDoors] TraderDoorResetIntervalSeconds not found in config.xml. Using default ResetIntervalSeconds=10");
            }

            Debug.Log("[DMCloseDoors] ResetIntervalSeconds=" + ResetIntervalSeconds);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[DMCloseDoors] Failed to load config.xml. Using default ResetIntervalSeconds=10. Error: " + ex);
        }
    }

    public static void ResetTraderDoorsIfOpen(World world)
    {
        if (!EnableInTraders || world == null)
            return;

        var traderAreas = GetTraderAreas(world).ToList();
        if (traderAreas.Count == 0)
            return;

        foreach (var traderArea in traderAreas)
        {
            if (traderArea == null)
                continue;

            try
            {
                //if (IsTraderAreaClosed(traderArea))
                //    continue;

                var setClosedMethod = GetSetClosedMethod(traderArea.GetType());
                if (setClosedMethod == null)
                    continue;

                object traderEntity = GetTraderEntity(traderArea);

                setClosedMethod.Invoke(traderArea, new object[] { world, true, traderEntity, false });
                setClosedMethod.Invoke(traderArea, new object[] { world, false, traderEntity, false });
            }
            catch (Exception ex)
            {
                if (DebugLog)
                    Debug.LogWarning("[DMCloseDoors] Failed to reset trader doors: " + ex);
            }
        }
    }

    private static bool IsTraderAreaClosed(object traderArea)
    {
        if (traderArea == null)
            return false;

        var type = traderArea.GetType();

        var prop = type.GetProperty("IsClosed", AnyInstance);
        if (prop != null && prop.PropertyType == typeof(bool))
        {
            try
            {
                return (bool)prop.GetValue(traderArea, null);
            }
            catch
            {
            }
        }

        var field = type.GetField("IsClosed", AnyInstance);
        if (field != null && field.FieldType == typeof(bool))
        {
            try
            {
                return (bool)field.GetValue(traderArea);
            }
            catch
            {
            }
        }

        return false;
    }

    private static IEnumerable<object> GetTraderAreas(World world)
    {
        if (world == null)
            yield break;

        foreach (var obj in EnumeratePossibleContainers(world))
        {
            if (obj == null)
                continue;

            foreach (var traderArea in EnumerateTraderAreasFromObject(obj))
                yield return traderArea;
        }
    }

    private static IEnumerable<object> EnumeratePossibleContainers(World world)
    {
        yield return world;

        var gm = GameManager.Instance;
        if (gm != null)
            yield return gm;
    }

    private static IEnumerable<object> EnumerateTraderAreasFromObject(object root)
    {
        var yielded = new HashSet<object>();

        foreach (var field in root.GetType().GetFields(AnyInstance))
        {
            object value;
            try
            {
                value = field.GetValue(root);
            }
            catch
            {
                continue;
            }

            foreach (var traderArea in ExtractTraderAreas(value))
            {
                if (yielded.Add(traderArea))
                    yield return traderArea;
            }
        }

        foreach (var prop in root.GetType().GetProperties(AnyInstance))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;

            object value;
            try
            {
                value = prop.GetValue(root, null);
            }
            catch
            {
                continue;
            }

            foreach (var traderArea in ExtractTraderAreas(value))
            {
                if (yielded.Add(traderArea))
                    yield return traderArea;
            }
        }
    }

    private static IEnumerable<object> ExtractTraderAreas(object value)
    {
        if (value == null)
            yield break;

        var type = value.GetType();

        if (type.Name == "TraderArea")
        {
            yield return value;
            yield break;
        }

        if (value is IEnumerable enumerable && !(value is string))
        {
            foreach (var item in enumerable)
            {
                if (item == null)
                    continue;

                if (item.GetType().Name == "TraderArea")
                    yield return item;
            }
        }
    }

    private static MethodInfo GetSetClosedMethod(Type traderAreaType)
    {
        return traderAreaType
            .GetMethods(AnyInstance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SetClosed")
                    return false;

                var p = m.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(World)
                    && p[1].ParameterType == typeof(bool)
                    && p[3].ParameterType == typeof(bool);
            });
    }

    private static object GetTraderEntity(object traderArea)
    {
        if (traderArea == null)
            return null;

        var type = traderArea.GetType();

        foreach (var propName in new[] { "Trader", "EntityTrader", "owningTrader" })
        {
            var prop = type.GetProperty(propName, AnyInstance);
            if (prop != null && typeof(EntityTrader).IsAssignableFrom(prop.PropertyType))
            {
                try
                {
                    return prop.GetValue(traderArea, null);
                }
                catch
                {
                }
            }
        }

        foreach (var fieldName in new[] { "Trader", "EntityTrader", "owningTrader" })
        {
            var field = type.GetField(fieldName, AnyInstance);
            if (field != null && typeof(EntityTrader).IsAssignableFrom(field.FieldType))
            {
                try
                {
                    return field.GetValue(traderArea);
                }
                catch
                {
                }
            }
        }

        foreach (var prop in type.GetProperties(AnyInstance))
        {
            if (typeof(EntityTrader).IsAssignableFrom(prop.PropertyType) && prop.GetIndexParameters().Length == 0)
            {
                try
                {
                    return prop.GetValue(traderArea, null);
                }
                catch
                {
                }
            }
        }

        foreach (var field in type.GetFields(AnyInstance))
        {
            if (typeof(EntityTrader).IsAssignableFrom(field.FieldType))
            {
                try
                {
                    return field.GetValue(traderArea);
                }
                catch
                {
                }
            }
        }

        return null;
    }
}