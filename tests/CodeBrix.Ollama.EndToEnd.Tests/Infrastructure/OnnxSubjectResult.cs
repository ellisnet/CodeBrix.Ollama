using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// What one subject's comparison measured: how far the two engines were apart, whether they made the same
/// greedy choice, how long a step took at each thread count, and what the process's memory did.
/// </summary>
public sealed class OnnxSubjectResult
{
    private readonly Dictionary<string, double> _milliseconds = new Dictionary<string, double>();
    private readonly Dictionary<int, double> _oracleMilliseconds = new Dictionary<int, double>();
    private readonly Dictionary<int, double> _worst = new Dictionary<int, double>();
    private readonly Dictionary<int, double> _worstCache = new Dictionary<int, double>();
    private readonly Dictionary<int, string> _worstName = new Dictionary<int, string>();
    private readonly Dictionary<int, int> _disagreements = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _cacheDisagreements = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _rows = new Dictionary<int, int>();

    /// <summary>Creates a result.</summary>
    /// <param name="subject">What the subject is called in the report.</param>
    /// <param name="graph">The logical name of the graph that was run.</param>
    public OnnxSubjectResult(string subject, string graph)
    {
        Subject = subject;
        Graph = graph;
    }

    /// <summary>What the subject is called in the report.</summary>
    public string Subject { get; }

    /// <summary>The logical name of the graph that was run.</summary>
    public string Graph { get; }

    /// <summary>How long the model took to load, at one thread.</summary>
    public double LoadMilliseconds { get; set; }

    /// <summary>The process's high-water mark of resident memory around the first load.</summary>
    public long PeakResidentBytes { get; set; }

    /// <summary>The process's resident memory once the load's own garbage has been collected.</summary>
    public long SettledResidentBytes { get; set; }

    /// <summary>The largest relative difference seen over both steps.</summary>
    public double Worst
    {
        get
        {
            double worst = 0;
            foreach (double value in _worst.Values)
            {
                if (value > worst) worst = value;
            }

            return worst;
        }
    }

    /// <summary>The largest relative difference over the cache, over both steps.</summary>
    public double WorstCache
    {
        get
        {
            double worst = 0;
            foreach (double value in _worstCache.Values)
            {
                if (value > worst) worst = value;
            }

            return worst;
        }
    }

    /// <summary>How many greedy choices differed over both steps.</summary>
    public int Disagreements
    {
        get
        {
            int total = 0;
            foreach (int value in _disagreements.Values) total += value;
            return total;
        }
    }

    /// <summary>Records how far the two engines were apart on one step.</summary>
    /// <param name="step">Which step.</param>
    /// <param name="worst">The largest relative difference over the outputs that are not the cache.</param>
    /// <param name="worstCache">The largest relative difference over the cache.</param>
    /// <param name="worstName">The output the first of those was on.</param>
    /// <param name="disagreements">How many greedy choices differed.</param>
    /// <param name="rows">How many greedy choices were compared.</param>
    /// <param name="cacheDisagreements">How many rows of the cache put their largest element elsewhere.</param>
    public void Note(
        int step, double worst, double worstCache, string worstName, int disagreements, int rows,
        int cacheDisagreements)
    {
        if (!_worst.TryGetValue(step, out double seen) || worst > seen)
        {
            _worst[step] = worst;
            _worstName[step] = worstName;
        }

        if (!_worstCache.TryGetValue(step, out double seenCache) || worstCache > seenCache)
        {
            _worstCache[step] = worstCache;
        }

        _disagreements[step] = disagreements;
        _cacheDisagreements[step] = cacheDisagreements;
        _rows[step] = rows;
    }

    /// <summary>Records how long one step took.</summary>
    /// <param name="threads">How many threads the managed engine used.</param>
    /// <param name="step">Which step.</param>
    /// <param name="managed">How long the managed engine took, in milliseconds.</param>
    /// <param name="oracle">How long onnxruntime took, in milliseconds.</param>
    public void Record(int threads, int step, double managed, double oracle)
    {
        _milliseconds[Key(threads, step)] = managed;
        _oracleMilliseconds[step] = oracle;
    }

    /// <summary>One line per step, for the report the test writes.</summary>
    /// <returns>The summary.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        foreach (int step in new SortedSet<int>(_worst.Keys))
        {
            if (text.Length > 0) text.Append('\n');
            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0,-28} {1,-22} step {2}  answer {3} ({4}/{5} differ)  cache {6} ({7} rows)  managed"
                + " {8:F1}/{9:F1} ms @1/@8  ort {10:F1} ms",
                Subject, Graph, step, OnnxAgreement.Number(_worst[step]), _disagreements[step], _rows[step],
                OnnxAgreement.Number(_worstCache[step]), _cacheDisagreements[step],
                Milliseconds(1, step), Milliseconds(8, step), Oracle(step));
        }

        return text.ToString();
    }

    private double Milliseconds(int threads, int step) =>
        _milliseconds.TryGetValue(Key(threads, step), out double value) ? value : 0;

    private double Oracle(int step) =>
        _oracleMilliseconds.TryGetValue(step, out double value) ? value : 0;

    private static string Key(int threads, int step) =>
        threads.ToString(CultureInfo.InvariantCulture) + "/" + step.ToString(CultureInfo.InvariantCulture);
}
