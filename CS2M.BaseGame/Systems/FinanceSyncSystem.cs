using System;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Unity.Entities;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    /// Server-side polling system: reads current tax rates and service budgets every
    /// ~5 seconds and broadcasts a FinanceSyncCommand to all clients whenever the
    /// values differ from the last broadcast.  Clients apply the command via
    /// FinanceSyncService.TryApplyFinance.
    /// </summary>
    public partial class FinanceSyncSystem : GameSystemBase
    {
        private const int BroadcastInterval = 300; // ~5 s at 60 fps

        private static readonly BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private int _counter;
        private float[] _lastTaxRates;
        private float[] _lastBudgets;

        private ComponentSystemBase _taxSystem;
        private ComponentSystemBase _financeSystem;
        private FieldInfo _taxRatesField;
        private FieldInfo _budgetsField;
        private bool _systemsResolved;

        protected override void OnUpdate()
        {
            if (Command.CurrentRole != MultiplayerRole.Server)
            {
                return;
            }

            _counter++;
            if (_counter < BroadcastInterval)
            {
                return;
            }

            _counter = 0;
            ResolveSystems();

            float[] taxRates = ReadFloatArray(_taxSystem, _taxRatesField);
            float[] budgets = ReadFloatArray(_financeSystem, _budgetsField);

            if (taxRates == null && budgets == null)
            {
                return;
            }

            if (FloatArraysEqual(taxRates, _lastTaxRates) && FloatArraysEqual(budgets, _lastBudgets))
            {
                return;
            }

            _lastTaxRates = taxRates != null ? (float[])taxRates.Clone() : null;
            _lastBudgets = budgets != null ? (float[])budgets.Clone() : null;

            Command.SendToAll(new FinanceSyncCommand
            {
                TaxSliderValues = taxRates,
                BudgetSliderValues = budgets,
                TransactionNonce = 0, // 0 bypasses nonce dedup — server broadcast
                RequestOnly = false
            });

            Log.Debug("FinanceSyncSystem: broadcast finance state to clients.");
        }

        private void ResolveSystems()
        {
            if (_systemsResolved)
            {
                return;
            }

            _systemsResolved = true;

            foreach (var s in World.Systems)
            {
                string name = s.GetType().Name;
                if (name == "TaxSystem")
                {
                    _taxSystem = s as ComponentSystemBase;
                    _taxRatesField = s.GetType().GetField("m_TaxRates", AllFlags)
                        ?? s.GetType().GetField("_taxRates", AllFlags);
                }
                else if (name == "CityFinanceSystem")
                {
                    _financeSystem = s as ComponentSystemBase;
                    _budgetsField = s.GetType().GetField("m_ServiceBudgets", AllFlags)
                        ?? s.GetType().GetField("_serviceBudgets", AllFlags);
                }
            }

            if (_taxSystem == null)
            {
                Log.Warn("FinanceSyncSystem: TaxSystem not found — tax sync disabled.");
            }

            if (_financeSystem == null)
            {
                Log.Warn("FinanceSyncSystem: CityFinanceSystem not found — budget sync disabled.");
            }
        }

        private static float[] ReadFloatArray(ComponentSystemBase system, FieldInfo field)
        {
            if (system == null || field == null)
            {
                return null;
            }

            try
            {
                object value = field.GetValue(system);
                if (value is float[] f)
                {
                    return f;
                }

                // CS2 might store as int[] (tax rates are 0-30 integer percentages)
                if (value is int[] intArr)
                {
                    float[] result = new float[intArr.Length];
                    for (int i = 0; i < intArr.Length; i++)
                    {
                        result[i] = intArr[i];
                    }
                    return result;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool FloatArraysEqual(float[] a, float[] b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 0.5f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
