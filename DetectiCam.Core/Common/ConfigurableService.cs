﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace DetectiCam.Core.Common
{
    public abstract class ConfigurableService<TService,TOption> where TService : ConfigurableService<TService, TOption>
                                                       where TOption : class, new()
    {
        protected ILogger Logger { get; }
        protected TOption Options { get; }

        public ConfigurableService(ILogger<TService> logger,
            IOptions<TOption> options)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (options is null) throw new ArgumentNullException(nameof(options));

            Logger = logger;

            try
            {
                Options = options.Value;
            }
            catch (OptionsValidationException ex)
            {
                foreach (var failure in ex.Failures)
                {
                    Logger.LogError(failure);
                }
                throw;
            }
        }
    }
}
