using System;
using System.Runtime.InteropServices;
using System.Text;

public class NeuralEngine : IDisposable
{
    private IntPtr _nativeHandle;
    private bool _disposed = false;

    // --- P/Invoke DLL Imports ---
    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CreateEngine(int capacity);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DestroyEngine(IntPtr handle);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void StepEngine(IntPtr handle, float signal, bool goodAction, bool badAction, bool novelState);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void StepCharEngine(IntPtr handle, char c, bool novelState);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SimulateEngine(IntPtr handle, float signal, bool goodAction, bool badAction, int iterations);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SpawnNeuronEngine(IntPtr handle);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GenerateCompletion(IntPtr handle, string prompt, StringBuilder buffer, int bufferSize, int maxChars);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool ExportSubnetEngine(IntPtr handle, string filepath, int startNeuron, int count);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool ImportSubnetEngine(IntPtr handle, string filepath);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SaveBrainFile(IntPtr handle, string filepath);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool LoadBrainFile(IntPtr handle, string filepath);

    // --- Constructor & Destructor ---
    public NeuralEngine(int capacity = 10)
    {
        _nativeHandle = CreateEngine(capacity);
    }

    // --- Public C# Wrapper Methods ---
    public void Step(float signal, bool goodAction, bool badAction, bool novelState)
    {
        StepEngine(_nativeHandle, signal, goodAction, badAction, novelState);
    }

    public void StepChar(char c, bool novelState)
    {
        StepCharEngine(_nativeHandle, c, novelState);
    }

    public void Simulate(float signal, bool goodAction, bool badAction, int iterations)
    {
        SimulateEngine(_nativeHandle, signal, goodAction, badAction, iterations);
    }

    public int SpawnNeuron()
    {
        return SpawnNeuronEngine(_nativeHandle);
    }

    public string Generate(string prompt, int maxChars = 50)
    {
        StringBuilder sb = new StringBuilder(512);
        GenerateCompletion(_nativeHandle, prompt, sb, sb.Capacity, maxChars);
        return sb.ToString();
    }

    public bool ExportSubnet(string filepath, int startNeuron, int count)
    {
        return ExportSubnetEngine(_nativeHandle, filepath, startNeuron, count);
    }

    public bool ImportSubnet(string filepath)
    {
        return ImportSubnetEngine(_nativeHandle, filepath);
    }

    public bool SaveState(string filepath)
    {
        return SaveBrainFile(_nativeHandle, filepath);
    }

    public bool LoadState(string filepath)
    {
        return LoadBrainFile(_nativeHandle, filepath);
    }

    // --- IDisposable Implementation ---
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_nativeHandle != IntPtr.Zero)
            {
                DestroyEngine(_nativeHandle);
                _nativeHandle = IntPtr.Zero;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    ~NeuralEngine()
    {
        Dispose();
    }
}