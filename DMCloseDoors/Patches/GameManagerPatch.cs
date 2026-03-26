using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
internal class Patch_GameManager_Update
{
    private static float _nextRun;

    static void Postfix()
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

        if (world.Players == null || world.Players.Count == 0)
            return;

        if (Time.time < _nextRun)
            return;

        _nextRun = Time.time + DMCloseDoors.TraderDoorResetIntervalSeconds;

        DMCloseDoors.ResetTraderDoorsIfOpen(world);
    }
}