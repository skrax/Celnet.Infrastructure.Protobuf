using System;
using FluentValidation;

namespace Celnet.Infrastructure.Protobuf
{
    public static class GuidValidator {
        public static IRuleBuilderOptions<T, string> IsValidGuid<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.Must(x => Guid.TryParse(x, out var guid) && guid != Guid.Empty);
        }
    }
}