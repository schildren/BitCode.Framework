namespace BitCode.Framework.Shared.Kernel;

public sealed record ValidationError : Error
{
    public Error[] Errors { get; }

    private ValidationError(Error[] errors)
        : base("Validation.General", "Se produjeron uno o más errores de validación.", ErrorType.Validation)
    {
        Errors = errors;
    }

    public static ValidationError FromErrors(Error[] errors) => new(errors);
}
