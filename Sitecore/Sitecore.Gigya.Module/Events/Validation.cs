﻿using Gigya.Module.Core.Connector.Logging;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Gigya.Module.Encryption;
using Sitecore.Gigya.Module.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Web;
using Sitecore.Gigya.Module.Helpers;
using Gigya.Module.Core.Connector.Helpers;

namespace Sitecore.Gigya.Module.Events
{
    public class Validation
    {
        public void OnItemSaving(object sender, EventArgs args)
        {
            var logger = new Logger(new SitecoreLogger());
            if (Context.ContentDatabase == null)
            {
                logger.Debug("ContentDatabase is null so aborting Sitecore.Gigya.Module.Events.Validate.OnItemSaving");
                return;
            }

            var eventArgs = args as Sitecore.Events.SitecoreEventArgs;
            Assert.IsNotNull(eventArgs, "eventArgs");

            Item updatedItem = eventArgs.Parameters[0] as Item;
            Assert.IsNotNull(updatedItem, "item");
            
            if (updatedItem.Database == null || updatedItem.Database.Name != Context.ContentDatabase.Name)
            {
                logger.Debug("Updated Database is null so aborting Sitecore.Gigya.Module.Events.Validate.OnItemSaving");
                return;
            }

            if (updatedItem.TemplateID != Constants.Templates.GigyaSettings)
            {
                return;
            }
            
            var settingsHelper = new Helpers.GigyaSettingsHelper();
            var mappedSettings = settingsHelper.Map(updatedItem, "validation-test", Context.ContentDatabase);

            try
            {
                settingsHelper.Validate(mappedSettings);
            }
            catch (Exception e)
            {
                CancelAndReturnError(eventArgs, e.Message);
                logger.Error("Validation error", e);
                return;
            }

            var apiHelper = new GigyaApiHelper(settingsHelper, logger);
            var plainTextApplicationSecret = settingsHelper.TryDecryptApplicationSecret(mappedSettings.ApplicationSecret, false);
            if (string.IsNullOrEmpty(plainTextApplicationSecret))
            {
                CancelAndReturnError(eventArgs, "Invalid application secret");
                return;
            }

            var testResponse = apiHelper.VerifySettings(mappedSettings, plainTextApplicationSecret);
            if (testResponse.GetErrorCode() != 0)
            {
                var gigyaErrorDetail = testResponse.GetString("errorDetails", string.Empty);
                var message = testResponse.GetErrorMessage();
                if (!string.IsNullOrEmpty(gigyaErrorDetail))
                {
                    message = string.Concat(message, ". ", gigyaErrorDetail);
                }

                CancelAndReturnError(eventArgs, message);
                logger.Error("Validation error");
                return;
            }
        }

        private static void CancelAndReturnError(Sitecore.Events.SitecoreEventArgs eventArgs, string message)
        {
            eventArgs.Result.Messages.Add(message);
            eventArgs.Result.Cancel = true;
            Context.ClientPage.ClientResponse.Alert(message);
        }
    }
}