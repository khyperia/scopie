using static Scopie.LibQhy;

namespace Scopie;

internal struct CameraControl
{
    private readonly IntPtr _qhyHandle;
    private readonly ControlId _controlId;
    private readonly bool _hasMinMaxStep;
    private double _min;
    private double _max;
    private double _step;

    public static bool TryGet(IntPtr qhyHandle, ControlId controlId, out CameraControl cameraControl)
    {
        if (IsQHYCCDControlAvailable(qhyHandle, controlId) != 0)
        {
            cameraControl = default;
            return false;
        }

        cameraControl = new CameraControl(qhyHandle, controlId);
        return true;
    }

    public CameraControl(IntPtr qhyHandle, ControlId controlId)
    {
        _qhyHandle = qhyHandle;
        _controlId = controlId;
        _hasMinMaxStep = GetQHYCCDParamMinMaxStep(qhyHandle, controlId, out _min, out _max, out _step) == 0;
    }

    public double Value
    {
        get => GetQHYCCDParam(_qhyHandle, _controlId);
        set => Check(SetQHYCCDParam(_qhyHandle, _controlId, value));
    }

    public string ToString(double value) =>
        _hasMinMaxStep ? $"{_controlId} = {value} ({_min}-{_max} by {_step})" : $"{_controlId} = {value} (readonly)";
}

internal struct CameraControlValue
{
    public CameraControl CameraControl;
    public double Value;

    public CameraControlValue(CameraControl cameraControl)
    {
        CameraControl = cameraControl;
        Value = cameraControl.Value;
    }

    public override string ToString() => CameraControl.ToString(Value);
}
