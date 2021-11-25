﻿using System;
using DefenseShields.Support;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
namespace DefenseShields
{
    public partial class DefenseShields
    {
        public void Draw(int onCount, bool sphereOnCamera)
        {
            _onCount = onCount;

            var renderId = MyGrid.Render.GetRenderObjectID();
            var percent = DsState.State.ShieldPercent;
            var reInforce = DsState.State.ReInforce;

            Vector3D impactPos;
            lock (HandlerImpact) impactPos = HandlerImpact.Active ? ComputeHandlerImpact() : WorldImpactPosition;
            WorldImpactPosition = impactPos;
            var activeVisible = DetermineVisualState(reInforce);
            WorldImpactPosition = Vector3D.NegativeInfinity;

            var kineticHit = EnergyHit == HitType.Kinetic;
            _localImpactPosition = Vector3D.NegativeInfinity;

            if (impactPos != Vector3D.NegativeInfinity)
            {
                if (_isServer && WebDamage && GridIsMobile)
                {
                    Vector3 pointVel;
                    var gridCenter = DetectionCenter;
                    MyGrid.Physics.GetVelocityAtPointLocal(ref gridCenter, out pointVel);
                    impactPos += (Vector3D)pointVel * Session.TwoStep;
                }

                if (kineticHit) KineticCoolDown = 0;
                else if (EnergyHit == HitType.Energy) EnergyCoolDown = 0;

                var cubeBlockLocalMatrix = MyGrid.PositionComp.LocalMatrixRef;
                var referenceWorldPosition = cubeBlockLocalMatrix.Translation;
                var worldDirection = impactPos - referenceWorldPosition;
                var localPosition = Vector3D.TransformNormal(worldDirection, MatrixD.Transpose(cubeBlockLocalMatrix));
                _localImpactPosition = localPosition;
            }

            EnergyHit = HitType.Kinetic;

            if (DsState.State.Online)
            {
                var prevlod = _prevLod;
                var lod = CalculateLod(_onCount);
                if (_shapeChanged || _updateRender || lod != prevlod)
                {
                    _updateRender = false;
                    _shapeChanged = false;

                    Icosphere.CalculateTransform(ShieldShapeMatrix, lod);
                    if (!GridIsMobile) Icosphere.ReturnPhysicsVerts(DetectionMatrix, ShieldComp.PhysicsOutside);
                }
                Icosphere.ComputeEffects(this, _localImpactPosition, sphereOnCamera, prevlod, percent, activeVisible, HitWave);

            }
            else if (_shapeChanged) _updateRender = true;

            var startPulse = Session.Instance.Tick20 && _toggle;
            var updated = Session.Instance.Tick300 || startPulse;
            var wasPulsing = _sidePulsing;

            if (updated && ShellActive != null && RedirectVisualUpdate())
                UpdateShieldRedirectVisuals();

            if (ShellActive != null && _sidePulsing)
                SidePulseRender();

            if (wasPulsing && !_sidePulsing)
                ClearSidePulse();


            if (Session.Instance.Settings.ClientConfig.ShowHitRings && Icosphere.ImpactRings.Count > 0)
            {
                var draw = sphereOnCamera && DsState.State.Online && !_viewInShield;
                Icosphere.Draw(renderId, draw, this);
            }

            HitWave = false;
        }

        public void DrawShieldDownIcon()
        {
            if (_tick % 60 != 0) HudCheck();
            var enemy = false;
            var relation = MyAPIGateway.Session.Player.GetRelationTo(MyCube.OwnerId);
            if (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.Enemies)
            {
                enemy = true;
            }

            var config = MyAPIGateway.Session.Config;
            if (!enemy && DsSet.Settings.SendToHud && !config.MinimalHud && Session.Instance.HudComp == this && !MyAPIGateway.Gui.IsCursorVisible)
            {
                UpdateIcon(false);
            }
        }

        private Vector3D ComputeHandlerImpact()
        {
            WebDamage = false;
            HandlerImpact.Active = false;
            if (HandlerImpact.HitBlock == null) return DetectionCenter;

            Vector3D originHit;
            HandlerImpact.HitBlock.ComputeWorldCenter(out originHit);

            var line = new LineD(HandlerImpact.Attacker.PositionComp.WorldAABB.Center, originHit);

            var testDir = Vector3D.Normalize(line.From - line.To);
            var ray = new RayD(line.From, -testDir);
            var matrix = DetectionMatrix;
            var invMatrix = MatrixD.Invert(matrix);
            var intersectDist = CustomCollision.IntersectEllipsoid(ref invMatrix, matrix, ref ray);
            var ellipsoid = intersectDist ?? line.Length;
            var shieldHitPos = line.From + (testDir * -ellipsoid);
            return shieldHitPos;
        }

        private static MyStringId GetHudIcon1FromFloat(float percent)
        {
            if (percent >= 99) return Session.Instance.HudIconHealth100;
            if (percent >= 90) return Session.Instance.HudIconHealth90;
            if (percent >= 80) return Session.Instance.HudIconHealth80;
            if (percent >= 70) return Session.Instance.HudIconHealth70;
            if (percent >= 60) return Session.Instance.HudIconHealth60;
            if (percent >= 50) return Session.Instance.HudIconHealth50;
            if (percent >= 40) return Session.Instance.HudIconHealth40;
            if (percent >= 30) return Session.Instance.HudIconHealth30;
            if (percent >= 20) return Session.Instance.HudIconHealth20;
            if (percent > 0) return Session.Instance.HudIconHealth10;
            return Session.Instance.HudIconOffline;
        }

        private static MyStringId GetHudIcon2FromFloat(float fState)
        {
            var slot = (int)Math.Floor(fState * 10);

            if (slot < 0) slot = (slot * -1) + 10;

            return Session.Instance.HudHealthHpIcons[slot];
        }

        private static MyStringId GetHudIcon3FromInt(int heat, bool flash)
        {
            if (heat == 100 && flash) return Session.Instance.HudIconHeat100;
            if (heat == 90) return Session.Instance.HudIconHeat90;
            if (heat == 80) return Session.Instance.HudIconHeat80;
            if (heat == 70) return Session.Instance.HudIconHeat70;
            if (heat == 60) return Session.Instance.HudIconHeat60;
            if (heat == 50) return Session.Instance.HudIconHeat50;
            if (heat == 40) return Session.Instance.HudIconHeat40;
            if (heat == 30) return Session.Instance.HudIconHeat30;
            if (heat == 20) return Session.Instance.HudIconHeat20;
            if (heat == 10) return Session.Instance.HudIconHeat10;
            return MyStringId.NullOrEmpty;
        }

        private bool DetermineVisualState(bool reInforce)
        {
            if (_tick60 || Session.Instance.HudIconReset) HudCheck();

            if (_tick20) _viewInShield = CustomCollision.PointInShield(MyAPIGateway.Session.Camera.WorldMatrix.Translation, DetectMatrixOutsideInv);
            if (reInforce)
                _hideShield = false;
            else if (_tick20 && _hideColor && !_supressedColor && _viewInShield)
            {
                _modelPassive = ModelLowReflective;
                UpdatePassiveModel();
                _supressedColor = true;
                _hideShield = false;
            }
            else if (_tick20 && _supressedColor && _hideColor && !_viewInShield)
            {
                SelectPassiveShell();
                UpdatePassiveModel();
                _supressedColor = false;
                _hideShield = false;
            }

            var forceShow = false;
            var relation = MyAPIGateway.Session.Player.GetRelationTo(MyCube.OwnerId);
            var promotionLevel = MyAPIGateway.Session.Player.PromoteLevel;
            var hostile = relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.Enemies || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership;
            var friend = relation == MyRelationsBetweenPlayerAndBlock.Owner || relation == MyRelationsBetweenPlayerAndBlock.Friends || relation == MyRelationsBetweenPlayerAndBlock.FactionShare;
            if (hostile || !friend && (promotionLevel == MyPromoteLevel.Moderator || promotionLevel == MyPromoteLevel.SpaceMaster || promotionLevel == MyPromoteLevel.Admin))
                forceShow = true;
            
            var config = MyAPIGateway.Session.Config;
            var drawIcon = !forceShow && DsSet.Settings.SendToHud && !config.MinimalHud && Session.Instance.HudComp == this && !MyAPIGateway.Gui.IsCursorVisible;
            if (drawIcon) UpdateIcon(reInforce);

            var clearView = !GridIsMobile || !_viewInShield;
            var activeInvisible = DsSet.Settings.ActiveInvisible;
            var activeVisible = !reInforce && ((!activeInvisible && clearView) || forceShow);
            var forceToVisOnHit = DsSet.Settings.Visible == 1 && forceShow;

            var visible = reInforce ? 1 : forceToVisOnHit ? 2 : DsSet.Settings.Visible;

            CalcualteVisibility(visible, activeVisible);

            return activeVisible;
        }

        private void CalcualteVisibility(long visible, bool activeVisible)
        {
            if (visible != 2)
                HitCoolDown = -11;
            else if (visible == 2 && WorldImpactPosition != Vector3D.NegativeInfinity)
                HitCoolDown = -10;
            else if (visible == 2 && HitCoolDown > -11)
                HitCoolDown++;

            if (HitCoolDown > 59) HitCoolDown = -11;

            // ifChecks: #1 FadeReset - #2 PassiveFade - #3 PassiveSet - #4 PassiveReset
            if (visible == 2 && !(visible != 0 && HitCoolDown > -1) && HitCoolDown != -11)
            {
                if (_shellPassive.Render.Transparency > 0 || _hideShield)
                    ResetShellRender(false);
            }
            else if (visible != 0 && HitCoolDown > -1)
            {
                ResetShellRender(true);
            }
            else if (visible != 0 && HitCoolDown == -11 && !_hideShield)
            {
                _hideShield = true;
                ResetShellRender(false, false);
            }
            else if ((visible == 0 || (!activeVisible && HitCoolDown == -10)) && _hideShield)
            {
                _hideShield = false;
                ResetShellRender(false);
            }
        }

        private void ResetShellRender(bool fade, bool updates = true)
        {
            _shellPassive.Render.UpdateRenderObject(false);
            _shellPassive.Render.Transparency = fade ? (HitCoolDown + 1) * 0.0166666666667f : 0f;
            if (updates) _shellPassive.Render.UpdateRenderObject(true);
        }

        private void ShellVisibility(bool forceInvisible = false)
        {
            if (forceInvisible)
            {
                _shellPassive.Render.UpdateRenderObject(false);
                ShellActive.Render.UpdateRenderObject(false);
                return;
            }

            if (DsState.State.Online && !DsState.State.Lowered && !DsState.State.Sleeping)
            {
                if (DsSet.Settings.Visible == 0) _shellPassive.Render.UpdateRenderObject(true);
                ShellActive.Render.UpdateRenderObject(true);
                ShellActive.Render.UpdateRenderObject(false);
            }
        }

        private int CalculateLod(int onCount)
        {
            var lod = 4;

            if (onCount > 9) lod = 2;
            else if (onCount > 3) lod = 3;

            _prevLod = lod;
            return lod;
        }

        public void HudCheck()
        {
            var playerEnt = MyAPIGateway.Session.ControlledObject?.Entity as MyEntity;
            if (playerEnt?.Parent != null) playerEnt = playerEnt.Parent;
            if (playerEnt == null)
            {
                Session.Instance.HudIconReset = true;
                Session.Instance.HudComp = null;
                Session.Instance.HudShieldDist = double.MaxValue;
                return;
            }

            var playerPos = playerEnt.PositionComp.WorldAABB.Center;
            var lastOwner = Session.Instance.HudComp;

            if (!CustomCollision.PointInShield(playerPos, DetectMatrixOutsideInv))
            {
                if (Session.Instance.HudComp != this) return;

                Session.Instance.HudIconReset = true;
                Session.Instance.HudComp = null;
                Session.Instance.HudShieldDist = double.MaxValue;
                return;
            }

            var distFromShield = Vector3D.DistanceSquared(playerPos, DetectionCenter);

            var takeOverHud = lastOwner == null || lastOwner != this && distFromShield <= Session.Instance.HudShieldDist;
            var lastIsStale = !takeOverHud && lastOwner != this && !CustomCollision.PointInShield(playerPos, lastOwner.DetectMatrixOutsideInv);
            if (takeOverHud || lastIsStale)
            {
                Session.Instance.HudShieldDist = distFromShield;
                Session.Instance.HudComp = this;
                Session.Instance.HudIconReset = true;
            }
        }

        private bool _toggle2;
        private void UpdateIcon(bool reInforce)
        {

            var camera = MyAPIGateway.Session.Camera;
            var newFov = camera.FovWithZoom;
            var aspectRatio = camera.ViewportSize.X / camera.ViewportSize.Y;

            if (Session.Instance.Tick180 || Session.Instance.Tick20 && _toggle2)
                _toggle2 = !_toggle2;

            var fov = Math.Tan(newFov * 0.5);
            var scaleFov = 0.1 * fov;
            var offset = new Vector2D(Session.Instance.Settings.ClientConfig.ShieldIconPos.X, Session.Instance.Settings.ClientConfig.ShieldIconPos.Y);
            offset.X *= scaleFov * aspectRatio;
            offset.Y *= scaleFov;
            var cameraWorldMatrix = MyAPIGateway.Session.Camera.WorldMatrix;
            var position = Vector3D.Transform(new Vector3D(offset.X, offset.Y, -.1), cameraWorldMatrix);

            var origin = position;
            var left = cameraWorldMatrix.Left;
            var up = cameraWorldMatrix.Up;
            var scale = (float)(scaleFov * (Session.Instance.Settings.ClientConfig.HudScale * 0.13f));

            var percent = DsState.State.ShieldPercent;
            var icon2FSelect = percent < 99 ? GetIconMeterfloat() : 0;
            var heat = DsState.State.Heat;
            var icon1 = GetHudIcon1FromFloat(percent);
            var icon2 = GetHudIcon2FromFloat(icon2FSelect);
            var icon3 = GetHudIcon3FromInt(heat, _count < 30);
            var showIcon2 = DsState.State.Online || DsState.State.Lowered;

            var mainColor = !DsState.State.Lowered ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.25f, 0.25f, 0.25f, 0.25f);

            Vector4 color;
            if (reInforce) color = Color.Green;
            else if (DsState.State.Lowered)
                color = new Vector4(0.25f, 0.25f, 0.25f, 0.25f);
            else color = GetDamageTypeColor();

            MyTransparentGeometry.AddBillboardOriented(icon1, color, origin, left, up, scale, BlendTypeEnum.PostPP); 
            if (showIcon2 && icon2 != MyStringId.NullOrEmpty) MyTransparentGeometry.AddBillboardOriented(icon2, mainColor, origin, left, up, scale, BlendTypeEnum.PostPP);
            if (icon3 != MyStringId.NullOrEmpty) MyTransparentGeometry.AddBillboardOriented(icon3, mainColor, origin, left, up, scale, BlendTypeEnum.PostPP);

            if (DsSet.Settings.SideShunting && DsState.State.Online)
            {
                foreach (var pair in Session.Instance.ShieldDirectedSidesDraw)
                {
                    var shunted = IsSideShunted(pair.Key);
                    var icon = pair.Value;
                    var sideColor = !shunted ? mainColor : _toggle2 ? new Vector4(1, 0, 0, 1) : new Vector4(0.01f, 0.01f, 0.01f, 0.01f);

                    MyTransparentGeometry.AddBillboardOriented(icon, sideColor, origin, left, up, scale, BlendTypeEnum.PostPP);
                }
            }
        }

        internal Vector4 GetDamageTypeColor()
        {
            if (_damageTypeBalance > 0) {
                var max = (float)KineticAvg;
                var min = (float)EnergyAvg;
                if (min <= 0)
                    return new Vector4(0f, 0, 1f, 1f);

                var value = (float)Math.Round(max / min);
                if (value < 1)
                    return Color.White;

                if (value >= 5)
                    return new Vector4(0f, 0, 1f, 1f);

                var mod = value * 0.2f;
                return new Vector4(mod, mod, 1f, 1f);
            }

            if (_damageTypeBalance < 0) {
                var max = (float)EnergyAvg;
                var min = (float)KineticAvg;

                if (min <= 0)
                    return new Vector4(1f, 0.5f, 0f, 1f);

                var value = (float)Math.Round(max / min);
                if (value < 1)
                    return Color.White;

                if (value >= 5)
                    return new Vector4(1f, 0.5f, 0f, 1f);

                var mod = value * 0.2f;
                var newColor = new Vector4(1f, 0.5f + (mod * 0.5f), mod, 1f);

                return newColor;
            }
            return Color.White;
        }

        internal Vector4 GetModulatorColor()
        {

            if (ShieldComp.Modulator != null && ShieldComp.Modulator.ModState.State.Online)
            {
                var damageMod = ShieldComp.Modulator.ModSet.Settings.ModulateDamage;
                if (damageMod > 100)
                {
                    var diff = Math.Abs(100f - (damageMod));
                    var ratio = diff / 80;

                    var mod = 1 - ratio;
                    return new Vector4(mod, mod, 1f, 1f);
                }

                if (damageMod < 100)
                {
                    var diff = Math.Abs(100f - (damageMod));
                    var ratio = diff / 80;

                    var mod = 1 - ratio;
                    var newColor = new Vector4(1f, 0.5f + (mod * 0.5f), mod, 1f);

                    return newColor;
                }
                return Color.White;
            }

            return Color.White;
        }

        private float GetIconMeterfloat()
        {
            if (_shieldPeakRate <= 0) return 0;
            var dps = _runningDamage;
            var hps = _runningHeal;
            var reduction = _expChargeReduction > 0 ? _shieldPeakRate / _expChargeReduction : _shieldPeakRate;
            if (hps > 0 && dps <= 0) return reduction / _shieldPeakRate;
            if (DsState.State.ShieldPercent > 99 || (hps <= 0 && dps <= 0)) return 0;
            if (hps <= 0) return 0.0999f;

            if (hps > dps)
            {
                var healing = MathHelper.Clamp(dps / hps, 0, reduction / _shieldPeakRate);
                return healing;
            }
            var damage = hps / dps;
            return -damage;
        }
    }
}
