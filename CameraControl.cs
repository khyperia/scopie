using static Scopie.LibQhy;

namespace Scopie;

internal readonly struct CameraControl
{
    private const double ExposureFactor = 1_000_000.0;
    private readonly IntPtr _qhyHandle;
    private readonly ControlId _controlId;
    private readonly bool _hasMinMaxStep;
    private readonly double _min;
    private readonly double _max;
    private readonly double _step;

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

    private CameraControl(IntPtr qhyHandle, ControlId controlId)
    {
        _qhyHandle = qhyHandle;
        _controlId = controlId;
        _hasMinMaxStep = GetQHYCCDParamMinMaxStep(qhyHandle, controlId, out _min, out _max, out _step) == 0;
        if (_controlId == ControlId.ControlExposure)
        {
            _min /= ExposureFactor;
            _max /= ExposureFactor;
            _step /= ExposureFactor;
        }
    }

    public double Value
    {
        get
        {
            var v = GetQHYCCDParam(_qhyHandle, _controlId);
            if (_controlId == ControlId.ControlExposure)
                v /= ExposureFactor;
            return v;
        }
        set
        {
            if (_controlId == ControlId.ControlExposure)
                value *= ExposureFactor;
            Check(SetQHYCCDParam(_qhyHandle, _controlId, value));
        }
    }

    public string ToString(double value)
    {
        var v = value.Equals(uint.MaxValue) ? -1 : value;
        return _hasMinMaxStep ? $"{_controlId} = {v} ({_min}-{_max} by {_step})" : $"{_controlId} = {value} (readonly)";
    }
}

internal readonly struct CameraControlValue(CameraControl cameraControl)
{
    private readonly double _value = cameraControl.Value;

    public override string ToString() => cameraControl.ToString(_value);
}
