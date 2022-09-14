using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

namespace DetectiCam.Core.Common
{
    public abstract class ConfigurableService<TService, TOption> where TService : ConfigurableService<TService, TOption>
                                                       where TOption : class, new()
    {
        protected ILogger Logger { get; }
        protected TOption Options { get; }

        protected ConfigurableService(ILogger<TService> logger,
            IOptions<TOption> options)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (options is null) throw new ArgumentNullException(nameof(options));

            Logger = logger;

            Options = GetValidatedOptions(options);
        }

        protected T GetValidatedOptions<T>(IOptions<T> options) where T: class,new()
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            try
            {
                return options.Value;
            }
            catch (OptionsValidationException ex)
            {
                foreach (var failure in ex.Failures)
                {
                    Logger.LogError("Invalid configuration:{failure}",failure);
                }
                throw;
            }
        }
    }
}
