using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System.Collections.Generic;
using System;
using System.Runtime.InteropServices;
using System.Numerics;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using Dalamud.Game.ClientState.Objects.Types;

namespace SliceIsRight;

public sealed class SliceIsRightPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SliceIsRight");

    private const ushort GoldSaucerTerritoryId = 144;
    private bool IsInGoldSaucer { get; set; }

    private readonly IDictionary<uint, DateTime> objectsAndSpawnTime = new Dictionary<uint, DateTime>();

    private readonly ISet<uint> objectsToMatch = new HashSet<uint>();

    private const float MaxDistance = 30f;

    private enum SliceIsRightModelID
    {
        ModelFallOneSide = 2010777,
        ModelFallTwoSide = 2010778,
        ModelFallRound = 2010779,
    }

    public SliceIsRightPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        PluginInterface.UiBuilder.Draw += DrawUI;
        ClientState.TerritoryChanged += TerritoryChanged;
        IsInGoldSaucer = ClientState.TerritoryType == GoldSaucerTerritoryId;
    }

    private void TerritoryChanged(ushort e)
    {
        IsInGoldSaucer = e == GoldSaucerTerritoryId;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        PluginInterface.UiBuilder.Draw -= DrawUI;
        ClientState.TerritoryChanged -= TerritoryChanged;
    }

    private void DrawUI()
    {

#if DEBUG
        if (ImGui.Begin("Debug", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs))
        {
            ImGui.Text("Current zone: " + ClientState.TerritoryType + " Player: " + ClientState.LocalPlayer + " ObjectTable is NULL: " + (ObjectTable == null ? "Yes" : "No"));
            ImGui.Text("Logged in: " + ClientState.IsLoggedIn + ", In GoldSaucer: " + (IsInGoldSaucer == true ? "Yes" : "No") + ", ObjectTable.Length = " + ObjectTable.Length);
            ImGui.End();
        }
#endif

        if (!ClientState.IsLoggedIn || !IsInGoldSaucer || ObjectTable == null)
            return;

        for (var index = 0; index < ObjectTable.Length; ++index)
        {
            IGameObject? obj = ObjectTable[index];
            if (obj == null || DistanceToPlayer(obj.Position) > MaxDistance)
                continue;

            var model = Marshal.ReadInt32(obj.Address + 128);
            if (obj.ObjectKind == ObjectKind.EventObj && (model >= (int)SliceIsRightModelID.ModelFallOneSide && model <= (int)SliceIsRightModelID.ModelFallRound))
            {
                RenderObject(index, obj, model);
            }
#if DEBUG   // locate Player and Draw FallRound
            else if (ClientState.LocalPlayer?.ObjectIndex == obj.ObjectIndex)
            {
                RenderObject(index, obj, (int)SliceIsRightModelID.ModelFallRound);
            }
            DebugObject(index, obj, model);
#endif
        }
    }

    private void RenderObject(int index, IGameObject obj, int model)
    {
        objectsToMatch.Remove((uint)obj.EntityId);

        if (objectsAndSpawnTime.TryGetValue(obj.EntityId, out DateTime spawnTime))
        {
            if (spawnTime.AddSeconds(5) > DateTime.Now)
                return;
        }
        else
        {
            objectsAndSpawnTime.Add(obj.EntityId, DateTime.Now);
            return;
        }


        ImGui.PushID("booWindow" + index);

        ImGui.PushStyleVar((ImGuiStyleVar)1, new Vector2(0.0f, 0.0f));
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(0.0f, 0.0f), ImGuiCond.None, new Vector2());
        ImGui.Begin("drawCtx" + index, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs);
        ImGui.SetWindowSize(ImGui.GetIO().DisplaySize);

        switch (model)
        {
            case (int)SliceIsRightModelID.ModelFallOneSide:
                DrawRectWorld(obj.Position, obj.Rotation + 1.570796f, 25f, 5f, ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.0f, 1f, 0.15f))));
                break;

            case (int)SliceIsRightModelID.ModelFallTwoSide:
                DrawRectWorld(obj.Position, obj.Rotation + 1.570796f, 25f, 5f, ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 1f, 0.0f, 0.15f))));
                DrawRectWorld(obj.Position, obj.Rotation - 1.570796f, 25f, 5f, ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 1f, 0.0f, 0.15f))));
                break;

            case (int)SliceIsRightModelID.ModelFallRound:
                DrawFilledCircleWorld(obj.Position, 11f, ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 0.4f))));
                break;
        }

        ImGui.End();
        ImGui.PopStyleVar();

        ImGui.PopID();
    }

    private void DebugObject(int index, IGameObject obj, int model)
    {
        if (GameGui.WorldToScreen(obj.Position, out var screenCoords))
        {
            // So, while WorldToScreen will return false if the point is off of game client screen, to
            // to avoid performance issues, we have to manually determine if creating a window would
            // produce a new viewport, and skip rendering it if so
            var distance = DistanceToPlayer(obj.Position);
            var objectText = $"{obj.Address.ToInt64():X}:{obj.EntityId:X}[{index}] - {obj.ObjectKind} - {model}: {obj.Name} - {distance:F2}";

            var screenPos = ImGui.GetMainViewport().Pos;
            var screenSize = ImGui.GetMainViewport().Size;

            var windowSize = ImGui.CalcTextSize(objectText);

            // Add some extra safety padding
            windowSize.X += ImGui.GetStyle().WindowPadding.X + 10;
            windowSize.Y += ImGui.GetStyle().WindowPadding.Y + 10;

            if (screenCoords.X + windowSize.X > screenPos.X + screenSize.X ||
                screenCoords.Y + windowSize.Y > screenPos.Y + screenSize.Y)
                return;

            if (distance > MaxDistance)
                return;

            ImGui.SetNextWindowPos(new Vector2(screenCoords.X, screenCoords.Y));

            ImGui.SetNextWindowBgAlpha(Math.Max(1f - (distance / MaxDistance), 0.2f));
            if (ImGui.Begin(
                    $"Actor{index}##ActorWindow{index}",
                    ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.AlwaysAutoResize |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoMouseInputs |
                    ImGuiWindowFlags.NoDocking |
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav))
                ImGui.Text(objectText);
            ImGui.End();
        }
    }

    private void DrawFilledCircleWorld(Vector3 center, float radius, uint colour)
    {
        int segmentCount = 100;
        for (int index = 0; index <= 2 * segmentCount; ++index)
        {
            GameGui.WorldToScreen(new Vector3(center.X + radius * (float)Math.Sin(Math.PI / segmentCount * index), center.Y, center.Z + radius * (float)Math.Cos(Math.PI / segmentCount * index)), out Vector2 vector2);
            ImGui.GetWindowDrawList().PathLineTo(vector2);
        }
        ImGui.GetWindowDrawList().PathFillConvex(colour);
    }

    private void DrawRectWorld(Vector3 center, float rotation, float length, float width, uint colour)
    {
        Vector2 displaySize = ImGui.GetIO().DisplaySize;
        Vector3 near1 = new Vector3(center.X + width * 0.5f * (float)Math.Sin(Math.PI / 2.0 + rotation), center.Y, center.Z + width * 0.5f * (float)Math.Cos(Math.PI / 2.0 + rotation));
        Vector3 near2 = new Vector3(center.X + width * 0.5f * (float)Math.Sin(rotation - Math.PI / 2.0), center.Y, center.Z + width * 0.5f * (float)Math.Cos(rotation - Math.PI / 2.0));
        Vector3 nearCenter = new Vector3(center.X, center.Y, center.Z);
        int rectangleCount = 20;

        var drawList = ImGui.GetWindowDrawList();
        for (int index = 1; index <= rectangleCount; ++index)
        {
            Vector3 far1 = new Vector3((float)(near1.X + length / (double)rectangleCount * Math.Sin(0.0 + rotation)), near1.Y, (float)(near1.Z + length / (double)rectangleCount * Math.Cos(0.0 + rotation)));
            Vector3 far2 = new Vector3((float)(near2.X + length / (double)rectangleCount * Math.Sin(0.0 + rotation)), near2.Y, (float)(near2.Z + length / (double)rectangleCount * Math.Cos(0.0 + rotation)));
            Vector3 farCenter = new Vector3((float)(nearCenter.X + length / (double)rectangleCount * Math.Sin(0.0 + rotation)), nearCenter.Y, (float)(nearCenter.Z + length / (double)rectangleCount * Math.Cos(0.0 + rotation)));

            GameGui.WorldToScreen(far2, out Vector2 vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            GameGui.WorldToScreen(farCenter, out vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            GameGui.WorldToScreen(far1, out vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            GameGui.WorldToScreen(near1, out vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            GameGui.WorldToScreen(nearCenter, out vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            GameGui.WorldToScreen(near2, out vector2_2);
            if (vector2_2.X > 0.0 & vector2_2.X < (double)displaySize.X | vector2_2.Y > 0.0 & vector2_2.Y < (double)displaySize.Y)
            {
                drawList.PathLineTo(new Vector2(vector2_2.X, vector2_2.Y));
            }

            drawList.PathFillConvex(colour);

            near1 = far1;
            near2 = far2;
            nearCenter = farCenter;
        }
    }

    private float DistanceToPlayer(Vector3 center)
    {
        return Vector3.Distance(ClientState.LocalPlayer?.Position ?? Vector3.Zero, center);
    }
}
