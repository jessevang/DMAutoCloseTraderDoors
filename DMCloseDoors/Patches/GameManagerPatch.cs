using HarmonyLib;
using System;
using UnityEngine;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
internal class Patch_GameManager_Update
{
    private static float _nextRunTime;

    static void Postfix()
    {
        try
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer)
                return;

            var gm = GameManager.Instance;
            var world = gm?.World;
            if (world == null)
                return;

            if (gm.gameStateManager == null || !gm.gameStateManager.IsGameStarted())
                return;

            if (Time.time < _nextRunTime)
                return;

            _nextRunTime = Time.time + DMCloseDoors.ResetIntervalSeconds;

            DMCloseDoors.ResetTraderDoorsIfOpen(world);
        }
        catch (Exception ex)
        {
            Debug.LogError("[DMCloseDoors] Error in Patch_GameManager_Update.Postfix: " + ex);
        }
    }
}