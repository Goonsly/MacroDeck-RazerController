using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace KeystoneDigital.RazerController.Actions;

/// <summary>Parameters, options and failure results shared by every Synapse-backed action.</summary>
internal static class DeviceOptions
{
	public const string DeviceParameter = "device";

	public static ActionParameter DeviceChoice() =>
		ActionParameter.DynamicChoice(DeviceParameter, label: Strings.Actions.Common.Device.Label(), description: Strings.Actions.Common.Device.Description(), required: true);

	/// <summary>
	/// Every matching device, plus the button's current serial when that device is gone, so a saved
	/// button keeps showing what it targets instead of silently losing its setting.
	/// </summary>
	public static DynamicOptionsResult Devices(SynapseService synapse, DynamicOptionsContext context, Func<RazerDeviceInfo, bool> include)
	{
		var options = synapse.Devices
			.Where(include)
			.Select(device => new ActionParameterOption { Value = device.Serial, Label = device.Name })
			.ToList();

		var current = ParameterReader.String(context.CurrentParameters ?? new Dictionary<string, object?>(), DeviceParameter);
		if (current is not null && options.All(option => option.Value != current))
		{
			options.Add(new ActionParameterOption { Value = current, Label = Strings.Actions.Common.DeviceNotConnected(current) });
		}

		return new DynamicOptionsResult { Options = options };
	}

	public static DynamicOptionsResult Profiles(SynapseService synapse, DynamicOptionsContext context)
	{
		var serial = ParameterReader.String(context.CurrentParameters ?? new Dictionary<string, object?>(), DeviceParameter);
		var device = serial is null ? null : synapse.FindBySerial(serial);

		var options = device is null
			? []
			: device.Profiles.Select(profile => new ActionParameterOption { Value = profile.Guid, Label = profile.Name }).ToList();

		// A saved profile that was deleted in Synapse stays visible instead of silently blanking the field.
		var current = ParameterReader.String(context.CurrentParameters ?? new Dictionary<string, object?>(), "profile");
		if (device is not null && current is not null && options.All(option => option.Value != current))
		{
			options.Add(new ActionParameterOption { Value = current, Label = Strings.Actions.Common.ProfileMissing(current) });
		}

		return new DynamicOptionsResult { Options = options };
	}

	/// <summary>Resolves the button's device, or the failure to report when it cannot be used.</summary>
	public static (RazerDeviceInfo? Device, ActionResult? Failure) Resolve(SynapseService synapse, IReadOnlyDictionary<string, object> parameters)
	{
		if (!synapse.IsReachable)
		{
			return (null, ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.SynapseUnavailable()));
		}

		var serial = ParameterReader.String(parameters, DeviceParameter);
		if (serial is null)
		{
			return (null, ActionResult.Failed(ActionErrorCodes.InvalidParameter, MacroDeckStrings.Validation.Required(Strings.Actions.Common.Device.Label())));
		}

		return synapse.FindBySerial(serial) is { } device
			? (device, null)
			: (null, ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.Errors.DeviceNotFound()));
	}

	public static ActionResult ToResult(CommandOutcome outcome, Func<ActionResult>? invalid = null) => outcome switch
	{
		CommandOutcome.Applied or CommandOutcome.NoChange => ActionResult.Success(),
		CommandOutcome.Invalid => invalid?.Invoke() ?? ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Actions.Errors.NotApplied()),
		CommandOutcome.NotConnected => ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.SynapseUnavailable()),
		CommandOutcome.DeviceNotFound => ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Actions.Errors.DeviceNotFound()),
		_ => ActionResult.Failed(ActionErrorCodes.Timeout, Strings.Actions.Errors.NotApplied()),
	};

	public static ActionResult Invalid(LocalizedText fieldLabel) =>
		ActionResult.Failed(ActionErrorCodes.InvalidParameter, MacroDeckStrings.Validation.Required(fieldLabel));
}
