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

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void StepTokenEngine(IntPtr handle, string token, bool novelState);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void TrainTextEngine(IntPtr handle, string text);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void TrainDialogueEngine(IntPtr handle, string prompt, string response);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SimulateEngine(IntPtr handle, float signal, bool goodAction, bool badAction, int iterations);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SpawnNeuronEngine(IntPtr handle);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int GenerateCompletion(IntPtr handle, string prompt, StringBuilder buffer, int bufferSize, int maxChars);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ProposeEngine(IntPtr handle, string prompt, StringBuilder buffer, int bufferSize, int maxChars, uint seed);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ProcessLiveDialogueEngine(IntPtr handle, string input, StringBuilder buffer, int bufferSize);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ProcessLiveDialogueWithLimitEngine(IntPtr handle, string input, StringBuilder buffer, int bufferSize, int maxChars);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int GetCurriculumFingerprintEngine(IntPtr handle, StringBuilder buffer, int capacity);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void SetCurriculumFingerprintEngine(IntPtr handle, string value);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void SetConversationTopicEngine(IntPtr handle, string value);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void TrainFromSourceEngine(IntPtr handle, string prompt, string passage, string source);
    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetAnswerStateEngine(IntPtr handle);
    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int GetConversationTopicEngine(IntPtr handle, StringBuilder buffer, int capacity);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void AdaptIdentityEngine(IntPtr handle, string name);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern bool ExportSubnetEngine(IntPtr handle, string filepath, int startNeuron, int count);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern bool ImportSubnetEngine(IntPtr handle, string filepath);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SaveBrainFile(IntPtr handle, string filepath);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool LoadBrainFile(IntPtr handle, string filepath);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetNeuronCount(IntPtr handle);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int GetVocabularyCount(IntPtr handle);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int FindWordNeuronEngine(IntPtr handle, string word);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern float WordConnectionWeightEngine(IntPtr handle, string from, string to);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern float GetActionValueEngine(IntPtr handle, string state, string action);

    [DllImport("DynamicNeuralEngine.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern void LearnActionValueEngine(IntPtr handle, string state, string action, float target, float learningRate);

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

    public void StepToken(string token, bool novelState = false)
    {
        StepTokenEngine(_nativeHandle, token, novelState);
    }

    public void TrainText(string text)
    {
        TrainTextEngine(_nativeHandle, text);
    }

    public void TrainDialogue(string prompt, string response)
    {
        TrainDialogueEngine(_nativeHandle, prompt, response);
    }

    public void LearnFromSource(string prompt, string passage, string source) => TrainFromSourceEngine(_nativeHandle, prompt, passage, source);
    public int AnswerState => GetAnswerStateEngine(_nativeHandle);
    public string ConversationTopic { get { var buffer = new StringBuilder(513); GetConversationTopicEngine(_nativeHandle, buffer, buffer.Capacity); return buffer.ToString(); } }

    public void Simulate(float signal, bool goodAction, bool badAction, int iterations)
    {
        SimulateEngine(_nativeHandle, signal, goodAction, badAction, iterations);
    }

    public int SpawnNeuron()
    {
        return SpawnNeuronEngine(_nativeHandle);
    }

    public string Generate(string prompt, int maxChars = 8192)
    {
        StringBuilder sb = ResponseBuffer(maxChars);
        GenerateCompletion(_nativeHandle, prompt, sb, sb.Capacity, maxChars);
        return sb.ToString();
    }

    public string Chat(string input, int maxChars = 8192)
    {
        StringBuilder sb = ResponseBuffer(maxChars);
        ProcessLiveDialogueWithLimitEngine(_nativeHandle, input, sb, sb.Capacity, maxChars);
        return sb.ToString();
    }

    public string Propose(string prompt, uint seed, int maxChars = 2048)
    {
        var buffer = ResponseBuffer(maxChars);
        ProposeEngine(_nativeHandle, prompt, buffer, buffer.Capacity, maxChars, seed);
        return buffer.ToString();
    }

    private StringBuilder ResponseBuffer(int maxChars)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxChars, 65536);
        return new StringBuilder(maxChars + 1);
    }

    public string CurriculumFingerprint
    {
        get { var buffer = new StringBuilder(1025); GetCurriculumFingerprintEngine(_nativeHandle, buffer, buffer.Capacity); return buffer.ToString(); }
        set { SetCurriculumFingerprintEngine(_nativeHandle, value); }
    }

    public void AdaptIdentity(string name)
    {
        AdaptIdentityEngine(_nativeHandle, name);
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

    public int NeuronCount => GetNeuronCount(_nativeHandle);
    public int VocabularyCount => GetVocabularyCount(_nativeHandle);
    public int WordNeuronId(string word) => FindWordNeuronEngine(_nativeHandle, word);
    public float WordConnectionWeight(string from, string to) => WordConnectionWeightEngine(_nativeHandle, from, to);
    public void SetConversationTopic(string input) => SetConversationTopicEngine(_nativeHandle, input);
    public float ActionValue(string state, string action) => GetActionValueEngine(_nativeHandle, state, action);
    public void LearnActionValue(string state, string action, float target, float learningRate = 0.25f) => LearnActionValueEngine(_nativeHandle, state, action, target, learningRate);

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
