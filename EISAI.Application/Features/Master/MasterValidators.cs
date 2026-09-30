using FluentValidation;

namespace Eisai.Application.Features.Master;

public sealed class CreateMasterCommandValidator : AbstractValidator<CreateMasterCommand>
{
    public CreateMasterCommandValidator()
    {
        RuleFor(command => command.MasterName)
            .Must(MasterSqlNames.IsMaster)
            .WithMessage("Master name must start with a letter and contain only letters, numbers, and underscores (max 40).");

        RuleFor(command => command.UniqueIdPrefix)
            .Must(prefix => MasterSqlNames.IsPrefix(prefix ?? string.Empty))
            .WithMessage("Unique id prefix must be 2 to 10 letters.");

        RuleFor(command => command.MenuHeading)
            .Must(IsMenuLabel)
            .WithMessage("Heading must be 2 to 80 letters, numbers, spaces, or & -.");

        RuleFor(command => command.MenuSubHeading)
            .Must(IsMenuLabel)
            .WithMessage("Sub heading must be 2 to 80 letters, numbers, spaces, or & -.");

        RuleFor(command => command.Columns)
            .NotEmpty()
            .WithMessage("At least one column is required.");

        RuleForEach(command => command.Columns).SetValidator(new MasterColumnInputValidator());

        RuleFor(command => command.Parent)
            .SetValidator(new MasterParentInputValidator()!)
            .When(command => command.Parent is not null);
    }

    private static bool IsMenuLabel(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length is >= 2 and <= 80 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^[\p{L}\p{N}][\p{L}\p{N} &-]{1,79}$");
    }
}

public sealed class AddMasterColumnCommandValidator : AbstractValidator<AddMasterColumnCommand>
{
    public AddMasterColumnCommandValidator()
    {
        RuleFor(command => command.MasterName).Must(MasterSqlNames.IsMaster);
        RuleFor(command => command.Column).NotNull().SetValidator(new MasterColumnInputValidator());
    }
}

public sealed class AlterMasterColumnCommandValidator : AbstractValidator<AlterMasterColumnCommand>
{
    public AlterMasterColumnCommandValidator()
    {
        RuleFor(command => command.MasterName).Must(MasterSqlNames.IsMaster);
        RuleFor(command => command.ColumnName).Must(MasterSqlNames.IsColumn);
        RuleFor(command => command.Column).NotNull().SetValidator(new MasterColumnInputValidator());
    }
}

public sealed class SetMasterParentCommandValidator : AbstractValidator<SetMasterParentCommand>
{
    public SetMasterParentCommandValidator()
    {
        RuleFor(command => command.MasterName).Must(MasterSqlNames.IsMaster);
        RuleFor(command => command.Parent).NotNull().SetValidator(new MasterParentInputValidator());
    }
}

public sealed class UpdateMasterParentCommandValidator : AbstractValidator<UpdateMasterParentCommand>
{
    public UpdateMasterParentCommandValidator()
    {
        RuleFor(command => command.MasterName).Must(MasterSqlNames.IsMaster);
        RuleFor(command => command.OnParentDelete)
            .Must(MasterSqlNames.IsParentDelete)
            .WithMessage("OnParentDelete must be restrict or setnull.");

        RuleFor(command => command)
            .Must(command => !(command.Required && MasterSqlNames.NormalizeParentDelete(command.OnParentDelete) == "setnull"))
            .WithMessage("setnull is allowed only when the parent is optional.");
    }
}

public sealed class MasterColumnInputValidator : AbstractValidator<MasterColumnInput>
{
    public MasterColumnInputValidator()
    {
        RuleFor(column => column).Custom((column, context) =>
        {
            var error = MasterColumnRules.Validate(column);
            if (error is not null)
            {
                context.AddFailure(error);
            }
        });
    }
}

public sealed class MasterParentInputValidator : AbstractValidator<MasterParentInput>
{
    public MasterParentInputValidator()
    {
        RuleFor(parent => parent.MasterName)
            .Must(MasterSqlNames.IsMaster)
            .WithMessage("Parent master name must start with a letter and contain only letters, numbers, and underscores (max 40).");

        RuleFor(parent => parent.OnParentDelete)
            .Must(MasterSqlNames.IsParentDelete)
            .WithMessage("OnParentDelete must be restrict or setnull.");

        RuleFor(parent => parent)
            .Must(parent => !(parent.Required && MasterSqlNames.NormalizeParentDelete(parent.OnParentDelete) == "setnull"))
            .WithMessage("setnull is allowed only when the parent is optional.");
    }
}
