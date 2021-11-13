﻿namespace DefenseShields
{
    using System;
    using Support;
    using Sandbox.Definitions;
    using Sandbox.Game.Entities;
    using Sandbox.ModAPI;
    using VRage.Game.Components;
    using VRage.Utils;
    using VRageMath;
    using MyVisualScriptLogicProvider = Sandbox.Game.MyVisualScriptLogicProvider;

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.Simulation, int.MaxValue - 3)]
    public partial class Session : MySessionComponentBase
    {
        #region BeforeStart
        public override void BeforeStart()
        {
            try
            {
                MpActive = MyAPIGateway.Multiplayer.MultiplayerActive;
                IsServer = MyAPIGateway.Multiplayer.IsServer;
                DedicatedServer = MyAPIGateway.Utilities.IsDedicated;
                HandlesInput = !IsServer || IsServer && !DedicatedServer;

                var env = MyDefinitionManager.Static.EnvironmentDefinition;
                if (env.LargeShipMaxSpeed > MaxEntitySpeed) MaxEntitySpeed = env.LargeShipMaxSpeed;
                else if (env.SmallShipMaxSpeed > MaxEntitySpeed) MaxEntitySpeed = env.SmallShipMaxSpeed;

                Log.Init("debugdevelop.log");
                Log.Line($"Logging Started: Server:{IsServer} - Dedicated:{DedicatedServer} - MpActive:{MpActive}");

                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, CheckDamage);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_ID, ReceivedPacket);

                if (!DedicatedServer && IsServer) Players.TryAdd(MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Session.Player);
                MyEntities.OnEntityRemove += OnEntityRemove;
                MyAPIGateway.Session.OnSessionReady += OnSessionReady;
                MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
                MyVisualScriptLogicProvider.PlayerRespawnRequest += PlayerConnected;
                if (!DedicatedServer)
                {
                    Password = MyStringId.GetOrCompute(Localization.GetText("TerminalPasswordTitle"));
                    PasswordTooltip = MyStringId.GetOrCompute(Localization.GetText("TerminalPasswordTooltip"));
                    ShieldFreq = MyStringId.GetOrCompute(Localization.GetText("TerminalShieldFreqTitle"));
                    ShieldFreqTooltip = MyStringId.GetOrCompute(Localization.GetText("TerminalShieldFreqTooltip"));
                    MyAPIGateway.TerminalControls.CustomControlGetter += CustomControls;
                }

                if (IsServer)
                {
                    Log.Line("LoadConf - Session: This is a server");
                    UtilsStatic.PrepConfigFile();
                    UtilsStatic.ReadConfigFile();
                }

                if (MpActive)
                {
                    SyncDist = MyAPIGateway.Session.SessionSettings.SyncDistance;
                    SyncDistSqr = SyncDist * SyncDist;
                    SyncBufferedDistSqr = SyncDistSqr + 250000;
                    if (Enforced.Debug >= 2) Log.Line($"SyncDistSqr:{SyncDistSqr} - SyncBufferedDistSqr:{SyncBufferedDistSqr} - DistNorm:{SyncDist}");
                }
                else
                {
                    SyncDist = MyAPIGateway.Session.SessionSettings.ViewDistance;
                    SyncDistSqr = SyncDist * SyncDist;
                    SyncBufferedDistSqr = SyncDistSqr + 250000;
                    if (Enforced.Debug >= 2) Log.Line($"SyncDistSqr:{SyncDistSqr} - SyncBufferedDistSqr:{SyncBufferedDistSqr} - DistNorm:{SyncDist}");
                }
                WebMonitor();

                if (!IsServer) RequestEnforcement(MyAPIGateway.Multiplayer.MyId);

                foreach (var mod in MyAPIGateway.Session.Mods)
                    if (mod.PublishedFileId == 540003236) ThyaImages = true;

                HudNotify = MyAPIGateway.Utilities.CreateNotification("", 2000, "UrlHighlight"); ;
                if (HandlesInput)
                    MyAPIGateway.Utilities.MessageEntered += ChatMessageSet;

                GenerateButtonMap();
                Settings = new ShieldSettings(this);

            }
            catch (Exception ex) { Log.Line($"Exception in BeforeStart: {ex}"); }
        }
        #endregion

        #region Simulation
        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (!MonitorTask.IsComplete)
                    MonitorTask.Wait();

                if (MonitorTask.IsComplete && MonitorTask.valid && MonitorTask.Exceptions != null)
                    TaskHasErrors(ref MonitorTask, "PTask");

                Timings();

                LogicUpdates();
                SplitMonitor();

                if (!ThreadEvents.IsEmpty)
                {
                    IThreadEvent tEvent;
                    while (ThreadEvents.TryDequeue(out tEvent)) tEvent.Execute();
                }
            }
            catch (Exception ex) { Log.Line($"Exception in SessionBeforeSim: {ex}"); }
        }

        public override void Simulate()
        {
            foreach (var s in ActiveShields.Keys)
                if (s.GridIsMobile && !s.Asleep) s.MobileUpdate();

            WebMonitor();
        }
        #endregion


        #region Draw
        public override void Draw()
        {
            if (DedicatedServer) return;
            try
            {
                var compCount = Controllers.Count;
                if (compCount == 0) return;

                if (Tick180)
                    ShellSortControllers(Controllers);
                
                if (SphereOnCamera.Length != compCount) Array.Resize(ref SphereOnCamera, compCount);

                if (_count == 0 && _lCount == 0) OnCountThrottle = false;
                var onCount = 0;
                for (int i = 0; i < compCount; i++)
                {
                    var s = Controllers[i];

                    if (s.DsState.State.Suspended) continue;


                    if (s.KineticCoolDown > -1) {
                        s.KineticCoolDown++;
                        if (s.KineticCoolDown == 6) s.KineticCoolDown = -1;
                    }

                    if (s.EnergyCoolDown > -1) {
                        s.EnergyCoolDown++;
                        if (s.EnergyCoolDown == 9) s.EnergyCoolDown = -1;
                    }

                    if (!s.WarmedUp || !IsServer && !s.ClientInitPacket || s.DsState.State.Lowered || s.DsState.State.Sleeping || s.DsState.State.Suspended || !s.DsState.State.EmitterLos) continue;

                    var sp = new BoundingSphereD(s.DetectionCenter, s.BoundingRange);
                    if (!MyAPIGateway.Session.Camera.IsInFrustum(ref sp))
                    {
                        SphereOnCamera[i] = false;
                        continue;
                    }
                    SphereOnCamera[i] = true;
                    if (!s.Icosphere.ImpactsFinished) onCount++;
                }

                if (onCount >= OnCount)
                {
                    OnCount = onCount;
                    OnCountThrottle = true;
                }
                else if (!OnCountThrottle && _count == 59 && _lCount == 9) OnCount = onCount;

                for (int i = 0; i < compCount; i++)
                {
                    var s = Controllers[i];
                    var drawSuspended = !s.WarmedUp || !IsServer && !s.ClientInitPacket || s.DsState.State.Sleeping || s.DsState.State.Suspended || !s.DsState.State.EmitterLos;

                    if (drawSuspended) continue;

                    if (s.DsState.State.Online)
                    {
                        if (SphereOnCamera[i] || s.Icosphere.ImpactRings.Count > 0) s.Draw(OnCount, SphereOnCamera[i]);
                        else if (s.Icosphere.ImpactsFinished)
                        {
                            if (s.WorldImpactPosition != Vector3D.NegativeInfinity)
                            {
                                s.Draw(OnCount, false);
                                s.Icosphere.ImpactPosState = Vector3D.NegativeInfinity;
                            }
                        }
                        else s.Icosphere.StepEffects();
                    }
                    else if (s.WarmedUp && SphereOnCamera[i]) s.DrawShieldDownIcon();
                }
            }
            catch (Exception ex) { Log.Line($"Exception in SessionDraw: {ex}"); }
        }


        public override void HandleInput()
        {
            try
            {
                if (HandlesInput && PlayersLoaded)
                {

                    if (ControlRequest != ControlQuery.None)
                        UpdateControlKeys();

                    UiInput.UpdateInputState();
                    if (MpActive)
                    {

                    }
                }
            }
            catch (Exception ex) { Log.Line($"Exception in HandleInput: {ex}"); }
        }

        #endregion
        #region Data
        public override void LoadData()
        {
            Instance = this;
            ApiServer.Load();
            MyAPIGateway.Gui.GuiControlCreated += MenuOpened;
            MyAPIGateway.Gui.GuiControlRemoved += MenuClosed;
            MyEntities.OnEntityCreate += OnEntityCreate;

        }

        protected override void UnloadData()
        {
            ApiServer.Unload();
            Instance = null;
            HudComp = null;
            Enforced = null;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_ID, ReceivedPacket);

            if (HandlesInput)
                MyAPIGateway.Utilities.MessageEntered -= ChatMessageSet;

            MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
            MyVisualScriptLogicProvider.PlayerRespawnRequest -= PlayerConnected;
            MyAPIGateway.Gui.GuiControlCreated -= MenuOpened;
            MyAPIGateway.Gui.GuiControlRemoved -= MenuClosed;
            MyEntities.OnEntityRemove -= OnEntityRemove;
            MyEntities.OnEntityCreate -= OnEntityCreate;

            MyAPIGateway.Session.OnSessionReady -= OnSessionReady;

            if (!DedicatedServer) MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControls;

            //Terminate();
            Log.Line("Logging stopped.");
            Log.Close();
        }
        #endregion
    }
}
