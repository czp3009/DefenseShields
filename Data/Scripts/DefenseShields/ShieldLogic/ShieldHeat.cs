﻿namespace DefenseShields
{
    using Support;

    public partial class DefenseShields
    {

        private void UpdateHeatRate()
        {
            var heat = DsState.State.Heat;
            heat /= 10;

            if (heat >= 10) ShieldChargeRate = 0;
            else
            {
                _expChargeReduction = ExpChargeReductions[heat];
                ShieldChargeRate /= _expChargeReduction;
            }
        }

        private void StepDamageState()
        {
            if (_isServer) Heating();

            if (_tick30)
            {
                _runningDamage = _dpsAvg.Add((int)_damageReadOut);
                _runningHeal = _hpsAvg.Add((int)(ShieldChargeRate * ConvToHp));
                _damageReadOut = 0;
            }
        }

        private void Heating()
        {
            var hp = ShieldMaxCharge * ConvToHp;

            var oldHeat = DsState.State.Heat;
            if (_tick30 && _damageReadOut > 0 && _heatCycle == -1)
            {
                _accumulatedHeat += _damageReadOut;
                _heatCycle = 0;
            }
            else if (_heatCycle > -1)
            {
                if (_tick30) _accumulatedHeat += _damageReadOut;
                _heatCycle++;
            }

            var ewarProt = DsState.State.EwarProtection && ShieldMode != ShieldType.Station;
            if (ewarProt && _heatCycle == 0)
            {
                _heatScaleHp = 0.1f;
                _heatScaleTime = 5;
            }
            else if (!ewarProt && _heatCycle == 0)
            {
                _heatScaleHp = 1f;
                _heatScaleTime = 1;
            }

            var heatScale = (ShieldMode == ShieldType.Station || DsSet.Settings.FortifyShield) && DsState.State.Enhancer ? Session.Enforced.HeatScaler * 2.75f : Session.Enforced.HeatScaler * 1f;
            var thresholdAmount = heatScale * _heatScaleHp;
            var nextThreshold = hp * thresholdAmount * (_currentHeatStep + 1);
            var currentThreshold = hp * thresholdAmount * _currentHeatStep;
            var scaledOverHeat = OverHeat / _heatScaleTime;
            var lastStep = _currentHeatStep == 10;
            var overloadStep = _heatCycle == scaledOverHeat;
            var scaledHeatingSteps = HeatingStep / _heatScaleTime;
            var afterOverload = _heatCycle > scaledOverHeat;
            var nextCycle = _heatCycle == (_currentHeatStep * scaledHeatingSteps) + scaledOverHeat;
            var overload = _accumulatedHeat > hp * thresholdAmount * 2;
            var pastThreshold = _accumulatedHeat > nextThreshold;
            var metThreshold = _accumulatedHeat > currentThreshold;
            var underThreshold = !pastThreshold && !metThreshold;
            var venting = lastStep && pastThreshold;
            var leftCritical = lastStep && _tick >= _heatVentingTick;
            var backOneCycles = ((_currentHeatStep - 1) * scaledHeatingSteps) + scaledOverHeat + 1;
            var backTwoCycles = ((_currentHeatStep - 2) * scaledHeatingSteps) + scaledOverHeat + 1;

            if (overloadStep)
            {
                if (overload)
                {
                    if (Session.Enforced.Debug == 3) Log.Line($"overh - stage:{_currentHeatStep + 1} - cycle:{_heatCycle} - resetCycle:xxxx - heat:{_accumulatedHeat} - threshold:{hp * thresholdAmount * 2}[{hp / hp * thresholdAmount * (_currentHeatStep + 1)}] - nThreshold:{hp * thresholdAmount * (_currentHeatStep + 2)} - ShieldId [{Shield.EntityId}]");
                    _currentHeatStep = 1;
                    DsState.State.Heat = _currentHeatStep * 10;
                    _accumulatedHeat = 0;
                }
                else
                {
                    if (Session.Enforced.Debug == 3) Log.Line($"under - stage:{_currentHeatStep} - cycle:{_heatCycle} - resetCycle:[-1] - heat:{_accumulatedHeat} - threshold:{nextThreshold} - ShieldId [{Shield.EntityId}]");
                    DsState.State.Heat = 0;
                    _currentHeatStep = 0;
                    _heatCycle = -1;
                    _accumulatedHeat = 0;
                }
            }
            else if (nextCycle && afterOverload && !lastStep)
            {
                if (_heatScaleTime == 5)
                {
                    if (_accumulatedHeat > 0)
                    {
                        _fallbackCycle = 1;
                        _accumulatedHeat = 0;
                    }
                    else _fallbackCycle++;
                }

                if (pastThreshold)
                {
                    if (Session.Enforced.Debug == 4) Log.Line($"incre - stage:{_currentHeatStep + 1} - cycle:{_heatCycle} - resetCycle:xxxx - heat:{_accumulatedHeat} - threshold:{nextThreshold}[{hp / hp * thresholdAmount * (_currentHeatStep + 1)}] - nThreshold:{hp * thresholdAmount * (_currentHeatStep + 2)} - ShieldId [{Shield.EntityId}]");
                    _currentHeatStep++;
                    DsState.State.Heat = _currentHeatStep * 10;
                    _accumulatedHeat = 0;
                    if (_currentHeatStep == 10) _heatVentingTick = _tick + CoolingStep;
                }
                else if (metThreshold)
                {
                    if (Session.Enforced.Debug == 4) Log.Line($"uncha - stage:{_currentHeatStep} - cycle:{_heatCycle} - resetCycle:{backOneCycles} - heat:{_accumulatedHeat} - threshold:{nextThreshold} - nThreshold:{hp * thresholdAmount * (_currentHeatStep + 2)} - ShieldId [{Shield.EntityId}]");
                    DsState.State.Heat = _currentHeatStep * 10;
                    _heatCycle = backOneCycles;
                    _accumulatedHeat = 0;
                }
                else _heatCycle = backOneCycles;

                if ((ewarProt && _fallbackCycle == FallBackStep) || (!ewarProt && underThreshold))
                {
                    if (_currentHeatStep == 0)
                    {
                        DsState.State.Heat = 0;
                        _currentHeatStep = 0;
                        if (Session.Enforced.Debug == 4) Log.Line($"nohea - stage:{_currentHeatStep} - cycle:{_heatCycle} - resetCycle:[-1] - heat:{_accumulatedHeat} - ShieldId [{Shield.EntityId}]");
                        _heatCycle = -1;
                        _accumulatedHeat = 0;
                        _fallbackCycle = 0;
                    }
                    else
                    {
                        if (Session.Enforced.Debug == 4) Log.Line($"decto - stage:{_currentHeatStep - 1} - cycle:{_heatCycle} - resetCycle:{backTwoCycles} - heat:{_accumulatedHeat} - threshold:{currentThreshold} - ShieldId [{Shield.EntityId}]");
                        _currentHeatStep--;
                        DsState.State.Heat = _currentHeatStep * 10;
                        _heatCycle = backTwoCycles;
                        _accumulatedHeat = 0;
                        _fallbackCycle = 0;
                    }
                }
            }
            else if (venting)
            {
                if (Session.Enforced.Debug == 4) Log.Line($"mainc - stage:{_currentHeatStep} - cycle:{_heatCycle} - resetCycle:xxxx - heat:{_accumulatedHeat} - threshold:{nextThreshold} - ShieldId [{Shield.EntityId}]");
                _heatVentingTick = _tick + CoolingStep;
                _accumulatedHeat = 0;
            }
            else if (leftCritical)
            {
                if (_currentHeatStep >= 10) _currentHeatStep--;
                if (Session.Enforced.Debug == 4) Log.Line($"leftc - stage:{_currentHeatStep} - cycle:{_heatCycle} - resetCycle:{backTwoCycles} - heat:{_accumulatedHeat} - threshold:{nextThreshold}[{hp / hp * thresholdAmount * (_currentHeatStep + 1)}] - nThreshold:{hp * thresholdAmount * (_currentHeatStep + 2)} - ShieldId [{Shield.EntityId}]");
                DsState.State.Heat = _currentHeatStep * 10;
                _heatCycle = backTwoCycles;
                _heatVentingTick = uint.MaxValue;
                _accumulatedHeat = 0;
            }

            if (_heatCycle > (HeatingStep * 10) + OverHeat && _tick >= _heatVentingTick) {
                if (Session.Enforced.Debug == 4) Log.Line($"HeatCycle over limit, resetting: heatCycle:{_heatCycle} - fallCycle:{_fallbackCycle}");
                _heatCycle = -1;
                _fallbackCycle = 0;
            }

            if (oldHeat != DsState.State.Heat) {
                ShieldChangeState();
            }
        }
    }
}