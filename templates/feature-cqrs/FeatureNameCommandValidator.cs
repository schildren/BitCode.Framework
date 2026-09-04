using FluentValidation;

namespace MyApp.Application.Features;

public class FeatureNameCommandValidator : AbstractValidator<FeatureNameCommand>
{
    public FeatureNameCommandValidator()
    {
        // RuleFor(c => c.Propiedad).NotEmpty();
    }
}
