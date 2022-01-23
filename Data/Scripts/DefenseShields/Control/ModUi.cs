namespace DefenseShields
{
    using Support;
    using Sandbox.Game.Entities;
    using Sandbox.ModAPI;

    internal static class ModUi
    {
        #region Create UI
        internal static void CreateUi(IMyTerminalBlock modualator)
        {
            Session.Instance.CreateModulatorUi(modualator);
            Session.Instance.ModDamage.Enabled = block => true;
            Session.Instance.ModDamage.Visible = ShowControl;
            Session.Instance.ModVoxels.Enabled = block => true;
            Session.Instance.ModVoxels.Visible = ShowVoxels;
            Session.Instance.ModGrids.Enabled = block => true;
            Session.Instance.ModGrids.Visible = ShowControl;
            Session.Instance.ModAllies.Enabled = block => true;
            Session.Instance.ModAllies.Visible = ShowControl;
            Session.Instance.ModEmp.Enabled = block => false;
            Session.Instance.ModEmp.Visible = ShowEMP;
            Session.Instance.ModReInforce.Enabled = block => true;
            Session.Instance.ModReInforce.Visible = ShowReInforce;
            Session.Instance.ModSep1.Visible = ShowControl;
            Session.Instance.ModSep2.Visible = ShowControl;
        }

        internal static bool ShowControl(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            var station = comp != null;
            return station;
        }

        internal static float GetDamage(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.ModulateDamage ?? 0;
        }

        internal static void SetDamage(IMyTerminalBlock block, float newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;

            if (Session.Instance.IsServer) 
                ComputeDamage(comp, newValue);
            
            comp.ModSet.Settings.ModulateDamage = (int)newValue;
            comp.SettingsUpdated = true;
            comp.ClientUiUpdate = true;
            ((MyCubeBlock)block).UpdateTerminal();
        }

        internal static void ComputeDamage(Modulators comp, float newValue)
        {
            if (newValue < 100)//modulating for kinetic protection
            {
                comp.ModState.State.ModulateEnergy = (100 - newValue) * 1.875f + 100;// this nets newValue 20 = ModEnergy 250%, newValue 99 = ModEnergy 102.5%
                comp.ModState.State.ModulateKinetic = newValue; //this is fine
            }
            else if (newValue > 100)//modulating for energy protection
            {
                comp.ModState.State.ModulateEnergy = 200 - newValue; //this is fine
                comp.ModState.State.ModulateKinetic = (newValue-100) * 1.875f + 100;// this nets newValue 180 = ModKinetic 250%, newValue 101 = ModKinetic 102.5%
            }
            else
            {
                comp.ModState.State.ModulateKinetic = newValue;
                comp.ModState.State.ModulateEnergy = newValue;
            }
            comp.ModState.State.ModulateDamage = (int)newValue;
        }

        internal static bool ShowVoxels(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp?.ShieldComp?.DefenseShields == null || comp.ShieldComp.DefenseShields.IsStatic) return false;

            return comp.ModState.State.Link;
        }

        internal static bool GetVoxels(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.ModulateVoxels ?? false;
        }

        internal static void SetVoxels(IMyTerminalBlock block, bool newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;
            comp.ModSet.Settings.ModulateVoxels = newValue;
            comp.ModSet.NetworkUpdate();
            comp.ModSet.SaveSettings();
        }

        internal static bool GetGrids(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.ModulateGrids ?? false;
        }

        internal static void SetGrids(IMyTerminalBlock block, bool newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;
            comp.ModSet.Settings.ModulateGrids = newValue;
            comp.ModSet.NetworkUpdate();
            comp.ModSet.SaveSettings();
        }

        internal static bool GetAllies(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.AllowAllies ?? false;
        }

        internal static void SetAllies(IMyTerminalBlock block, bool newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;
            comp.ModSet.Settings.AllowAllies = newValue;
            comp.ModSet.NetworkUpdate();
            comp.ModSet.SaveSettings();
        }

        internal static bool ShowEMP(IMyTerminalBlock block)
        {
            return false;
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp?.ShieldComp?.DefenseShields == null || comp.ShieldComp.DefenseShields.IsStatic) return false;

            return comp.EnhancerLink;
        }

        internal static bool GetEmpProt(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.EmpEnabled ?? false;
        }

        internal static void SetEmpProt(IMyTerminalBlock block, bool newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;
            comp.ModSet.Settings.EmpEnabled = newValue;
            comp.ModSet.NetworkUpdate();
            comp.ModSet.SaveSettings();
        }

        internal static bool ShowReInforce(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp?.ShieldComp?.DefenseShields == null || comp.ShieldComp.DefenseShields.IsStatic)
                return false;

            return comp.EnhancerLink;
        }

        internal static bool GetReInforceProt(IMyTerminalBlock block)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            return comp?.ModSet.Settings.ReInforceEnabled ?? false;
        }

        internal static void SetReInforceProt(IMyTerminalBlock block, bool newValue)
        {
            var comp = block?.GameLogic?.GetAs<Modulators>();
            if (comp == null) return;
            comp.ModSet.Settings.ReInforceEnabled = newValue;
            comp.ModSet.NetworkUpdate();
            comp.ModSet.SaveSettings();
        }
        #endregion
    }
}
