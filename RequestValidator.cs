using Celnet.Infrastructure.Protobuf.Domain;
using FluentValidation;

namespace Celnet.Infrastructure.Protobuf
{
    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .NotNull()
                .IsValidGuid();

            RuleFor(x => x.Method)
                .IsInEnum();

            RuleFor(x => x.Route)
                .NotEmpty()
                .NotNull();
        }
    }
}