using Celnet.Infrastructure.Protobuf.Domain;
using FluentValidation;

namespace Celnet.Infrastructure.Protobuf
{
    public class ResponseValidator : AbstractValidator<Response>
    {
        public ResponseValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty()
                .NotNull()
                .IsValidGuid();

            RuleFor(x => x.RequestId)
                .NotEmpty()
                .NotNull()
                .IsValidGuid();

            RuleFor(x => x.Route)
                .NotEmpty()
                .NotNull();

            RuleFor(x => x.Body)
                .NotNull();
        }
    }
}