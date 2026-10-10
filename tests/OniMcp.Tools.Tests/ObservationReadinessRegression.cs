using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using OniMcp.Tools;

internal static class ObservationReadinessRegression
{
    private static int checks;

    internal static void Run()
    {
        Check(JObject.Parse(JsonConvert.SerializeObject(new NullablePropertyFixture(), McpJsonUtil.Settings))["Value"] == null,
            "production settings still omit null object properties");
        // Json.NET ignores null object properties, but preserves null dictionary
        // entries, including dictionaries nested in the public response arrays.
        var pending = new Dictionary<string, object>
        {
            ["connected"] = null,
            ["circuitId"] = null,
            ["currentValue"] = null,
            ["formattedCurrentValue"] = null,
            ["networkPending"] = true,
            ["samplePending"] = true
        };
        var response = new Dictionary<string, object>
        {
            ["items"] = new[] { pending }
        };
        var wire = JObject.Parse(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
        foreach (string field in new[] { "connected", "circuitId", "currentValue", "formattedCurrentValue" })
            Check(wire["items"][0][field]?.Type == JTokenType.Null,
                "pending dictionary field survives production serialization: " + field);
        Check((bool)wire["items"][0]["networkPending"] && (bool)wire["items"][0]["samplePending"],
            "pending markers remain visible on the wire");

        var sensor = new LogicTemperatureSensor();
        Check(!ThresholdReading.Current(sensor).HasValue, "new sensor must not report zero Kelvin");
        sensor.SetSamples(new[] { 290f, 291, 0, 0, 0, 0, 0, 0 }, 290);
        Check(!ThresholdReading.Current(sensor).HasValue, "partial sample buffer remains unknown");
        sensor.SetSamples(Enumerable.Repeat(290f, 8).ToArray(), 0);
        Check(!ThresholdReading.Current(sensor).HasValue, "unpublished average remains unknown");
        sensor.SetSamples(Enumerable.Repeat(290f, 8).ToArray(), 290);
        Check(ThresholdReading.Current(sensor) == 290, "ready sensor retains native Kelvin reading");
        sensor.SetSamples(new[] { 290f, 290, 0, 0, 0, 0, 0, 0 }, 72.5f);
        Check(!ThresholdReading.Current(sensor).HasValue, "threshold change during warm-up publishes an incomplete average");
        sensor.SetSamples(Enumerable.Repeat(290f, 8).ToArray(), 72.5f, 8);
        Check(!ThresholdReading.Current(sensor).HasValue, "filled buffer cannot validate an older partial average");
        sensor.SetSamples(Enumerable.Repeat(290f, 8).ToArray(), 290, 0);
        Check(ThresholdReading.Current(sensor) == 290, "next native publication clears warm-up pending state");
        sensor.SetSamples(Enumerable.Repeat(291f, 8).ToArray(), 290, 3);
        Check(ThresholdReading.Current(sensor) == 290, "later partial rounds retain the published average");
        sensor.SetSamples(Enumerable.Repeat(290f, 8).ToArray(), 290, -1);
        Check(!ThresholdReading.Current(sensor).HasValue, "invalid native sample counter fails closed");
        sensor.SetSamples(null, 290);
        Check(!ThresholdReading.Current(sensor).HasValue, "missing sample buffer fails closed");
        sensor.SetSamples(new[] { 290f }, 290);
        Check(!ThresholdReading.Current(sensor).HasValue, "unexpected sample-buffer layout fails closed");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            sensor.SetSamples(Enumerable.Repeat(invalid, 8).ToArray(), 290);
            Check(!ThresholdReading.Current(sensor).HasValue, "nonfinite samples remain unknown");
            Check(!ThresholdReading.Current(new TestThreshold { CurrentValue = invalid }).HasValue,
                "nonfinite native readings remain unknown");
        }
        Check(ThresholdReading.Current(new TestThreshold { CurrentValue = 0 }) == 0,
            "zero is valid for ordinary thresholds");
        Check(ThresholdReading.Current(new TestThreshold { CurrentValue = -10 }) == -10,
            "ordinary thresholds retain finite negative values");

        try
        {
            var connection = new TestCircuitConnection();
            Game.Instance = null;
            Check(PowerConnectionReadiness.Pending(connection, 1), "missing game graph is unknown");
            Check(!PowerConnectionReadiness.ReadCircuitId(() => { throw new InvalidOperationException("unsafe getter"); }).HasValue,
                "missing game does not evaluate native circuit getter");
            Game.Instance = new Game();
            Check(PowerConnectionReadiness.Pending(connection, 1), "missing manager is unknown");
            Check(!PowerConnectionReadiness.ReadCircuitId(() => { throw new InvalidOperationException("unsafe getter"); }).HasValue,
                "missing manager does not evaluate native circuit getter");
            Game.Instance.circuitManager = new CircuitManager { Circuit = 1 };
            Check(PowerConnectionReadiness.Pending(connection, 1), "missing electrical system is unknown");
            Check(!PowerConnectionReadiness.ReadCircuitId(() => { throw new InvalidOperationException("unsafe getter"); }).HasValue,
                "missing electrical system does not evaluate native circuit getter");
            Game.Instance.electricalConduitSystem = new TestElectricalSystem();
            int getterCalls = 0;
            Check(PowerConnectionReadiness.ReadCircuitId(() => { getterCalls++; return 1; }) == 1 && getterCalls == 1,
                "available graph evaluates the native getter exactly once");
            Check(!PowerConnectionReadiness.Pending(connection, 1), "settled matching graph is known");
            Game.Instance.electricalConduitSystem.IsDirty = true;
            Check(PowerConnectionReadiness.Pending(connection, 1), "dirty conduit graph is pending");
            Game.Instance.electricalConduitSystem.IsDirty = false;
            Game.Instance.circuitManager.DirtyForFixture = true;
            Check(PowerConnectionReadiness.Pending(connection, 1), "dirty circuit graph is pending");
            Game.Instance.circuitManager.DirtyForFixture = false;
            Check(PowerConnectionReadiness.Pending(connection, 2), "stale component ID is pending");
            Game.Instance.circuitManager.Circuit = ushort.MaxValue;
            Check(!PowerConnectionReadiness.Pending(connection, ushort.MaxValue),
                "settled disconnected component is known");
            Check(PowerConnectionReadiness.Pending(null, ushort.MaxValue), "missing component is unknown");
        }
        finally
        {
            Game.Instance = null;
        }
        Console.WriteLine("Observation readiness: " + checks + " checks passed");
    }

    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(description);
    }

    private sealed class NullablePropertyFixture
    {
        public string Value { get; set; }
    }
}

// Host fixtures model only the native surface consumed by the production helpers.
internal interface IThresholdSwitch { float CurrentValue { get; } }
internal class TestThreshold : IThresholdSwitch { public float CurrentValue { get; set; } }
internal sealed class LogicTemperatureSensor : TestThreshold
{
    private float[] temperatures = new float[8];
    private int simUpdateCounter;
    internal void SetSamples(float[] samples, float average, int collected = 0)
    {
        temperatures = samples;
        CurrentValue = average;
        simUpdateCounter = collected;
    }
    internal float[] SamplesForFixture => temperatures;
    internal int CounterForFixture => simUpdateCounter;
}

internal interface ICircuitConnected { }
internal sealed class TestCircuitConnection : ICircuitConnected { }
internal sealed class TestElectricalSystem { internal bool IsDirty { get; set; } }
internal sealed class Game
{
    internal static Game Instance { get; set; }
    internal CircuitManager circuitManager { get; set; }
    internal TestElectricalSystem electricalConduitSystem { get; set; }
}
internal sealed class CircuitManager
{
    private bool dirty;
    internal bool DirtyForFixture { get => dirty; set => dirty = value; }
    internal ushort Circuit { get; set; }
    internal ushort GetCircuitID(ICircuitConnected connection) => Circuit;
}
