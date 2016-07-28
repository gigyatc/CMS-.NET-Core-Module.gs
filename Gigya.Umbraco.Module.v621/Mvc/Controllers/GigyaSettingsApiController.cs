﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

using Umbraco.Web.WebApi;
using Umbraco.Web.Editors;
using Umbraco.Core.Persistence;
using Umbraco.Web.Mvc;
using Gigya.Umbraco.Module.v621.Mvc.Models;
using Gigya.Umbraco.Module.v621.Data;
using Gigya.Umbraco.Module.v621.Helpers;
using Gigya.Module.Core.Data;
using Gigya.Module.Core.Connector.Encryption;
using Newtonsoft.Json;
using Gigya.Module.Core.Connector.Models;
using Newtonsoft.Json.Serialization;
using Gigya.Module.Core.Connector.Helpers;
using Gigya.Module.Core.Connector.Logging;
using Gigya.Umbraco.Module.v621.Connector;

using Core = Gigya.Module.Core;
using Umbraco.Core;
using Umbraco.Web.UI.Pages;
using System.Web.UI;
using System.Web;
using Umbraco.Web.UI;

namespace Gigya.Umbraco.Module.v621.Mvc.Controllers
{
    [PluginController("Gigya")]
    public class GigyaSettingsApiController : UmbracoAuthorizedApiController
    {
        /// <summary>
        /// Gets all the settings data required for the client.
        /// </summary>
        /// <param name="id">Id of the homepage or -1 if global settings.</param>
        public GigyaSettingsApiResponseModel Get(int id)
        {
            var settingsHelper = new Umbraco.Module.v621.Helpers.GigyaSettingsHelper();
            var data = settingsHelper.Get(id);
            var model = GetModel(id, data);

            var wrappedModel = new GigyaSettingsApiResponseModel
            {
                Settings = model,
                Data = new GigyaConfigModel
                {
                    Languages = GigyaLanguageHelper.Languages.Select(i => new GigyaLanguageModel { Code = i.Key, Name = i.Value }).ToList()
                }
            };

            wrappedModel.Data.LanguageOptions = wrappedModel.Data.Languages.ToList();
            wrappedModel.Data.LanguageOptions.Insert(0, new GigyaLanguageModel
            {
                Code = Core.Constants.Languages.Other,
                Name = Core.Constants.Languages.Other
            });
            wrappedModel.Data.LanguageOptions.Insert(0, new GigyaLanguageModel
            {
                Code = Core.Constants.Languages.Auto,
                Name = Core.Constants.Languages.AutoName
            });

            return wrappedModel;
        }

        private GigyaSettingsModel GetModel(int id, IGigyaModuleSettings data)
        {
            var model = Map(data);

            model.Inherited = model.Id != id;
            model.Id = id;

            ApplyDefaults(ref model, data);
            return model;
        }

        private void ApplyDefaults(ref GigyaSettingsModel model, IGigyaModuleSettings settings)
        {
            if (string.IsNullOrEmpty(model.Language.Code))
            {
                model.Language.Code = Core.Constants.Languages.Auto;
            }
        }

        public GigyaSettingsResponseModel Save(GigyaSettingsModel model)
        {
            var response = new GigyaSettingsResponseModel();
            if (!ModelState.IsValid)
            {
                var errorList = ModelState.Values.SelectMany(m => m.Errors)
                                     .Select(e => e.ErrorMessage)
                                     .ToList();

                response.Error = string.Join(" ", errorList);
                return response;
            }
            
            var settingsHelper = new Umbraco.Module.v621.Helpers.GigyaSettingsHelper();
            if (model.Inherited && model.Id > 0)
            {
                settingsHelper.Delete(model.Id);
                response.Success = true;

                // return global settings to refresh client
                var globalData = settingsHelper.Get(model.Id);
                var globalModel = GetModel(model.Id, globalData);
                response.Settings = globalModel;
                return response;
            }
            
            var settings = settingsHelper.GetRaw(model.Id);

            // update all fields
            settings.ApiKey = model.ApiKey;
            settings.DebugMode = model.DebugMode;
            settings.ApplicationKey = model.ApplicationKey;
            settings.DataCenter = !string.IsNullOrEmpty(model.DataCenter) && model.DataCenter != Core.Constants.DataCenter.Other ? model.DataCenter : model.DataCenterOther;
            settings.EnableRaas = model.EnableRaas;
            settings.GlobalParameters = model.GlobalParameters;
            settings.Language = !string.IsNullOrEmpty(model.Language.Code) && model.Language.Code != Core.Constants.Languages.Other ? model.Language.Code : model.LanguageOther;
            settings.LanguageFallback = model.LanguageFallback.Code;
            settings.MappingFields = JsonConvert.SerializeObject(model.MappingFields);
            settings.RedirectUrl = model.RedirectUrl;
            settings.LogoutUrl = model.LogoutUrl;
            settings.SessionTimeout = model.SessionTimeout;

            // application secret that we will use to validate the settings - store this in a separate var as it's unencrypted
            string plainTextApplicationSecret = string.Empty;

            // check if user can view application secret
            if (!string.IsNullOrEmpty(model.ApplicationSecret))
            {
                plainTextApplicationSecret = model.ApplicationSecret;
                var canViewApplicationSecret = (UmbracoUser.UserType.Name == Constants.UserTypes.Admin) || User.IsInRole(Constants.Roles.GigyaAdmin);
                if (canViewApplicationSecret)
                {
                    if (!Encryptor.IsConfigured)
                    {
                        response.Error = "Encryption key not specified. Refer to installation guide.";
                        return response;
                    }
                    settings.ApplicationSecret = Encryptor.Encrypt(model.ApplicationSecret);
                }
            }

            if (string.IsNullOrEmpty(plainTextApplicationSecret) && Encryptor.IsConfigured && !string.IsNullOrEmpty(settings.ApplicationSecret))
            {
                plainTextApplicationSecret = Encryptor.Decrypt(settings.ApplicationSecret);
            }

            var mappedSettings = Map(settings);
            mappedSettings.ApplicationSecret = plainTextApplicationSecret;

            try
            {
                // validate input
                settingsHelper.Validate(mappedSettings);
            }
            catch (Exception e)
            {
                response.Error = e.Message;
                return response;
            }

            var logger = new Logger(new UmbracoLogger());

            // verify settings are correct
            var apiHelper = new GigyaApiHelper(settingsHelper, logger);
            var testResponse = apiHelper.VerifySettings(mappedSettings, plainTextApplicationSecret);
            if (testResponse.GetErrorCode() != 0)
            {
                response.Error = "Error: " + testResponse.GetErrorMessage();
                return response;
            }

            settingsHelper.Save(settings);

            response.Success = true;
            return response;
        }

        private IGigyaModuleSettings Map(GigyaUmbracoModuleSettings settings)
        {
            var model = new GigyaModuleSettings
            {
                ApiKey = settings.ApiKey,
                ApplicationSecret = settings.ApplicationSecret,
                ApplicationKey = settings.ApplicationKey,
                DataCenter = settings.DataCenter,
                DebugMode = settings.DebugMode,
                EnableRaas = settings.EnableRaas,
                GlobalParameters = settings.GlobalParameters,
                Id = settings.Id,
                Language = settings.Language,
                LanguageFallback = settings.LanguageFallback,
                LogoutUrl = settings.LogoutUrl,
                MappingFields = settings.MappingFields,
                RedirectUrl = settings.RedirectUrl,
                SessionTimeout = settings.SessionTimeout
            };

            return model;
        }

        private GigyaSettingsModel Map(IGigyaModuleSettings settings)
        {
            var language = new GigyaLanguageModel();
            language.Code = string.IsNullOrEmpty(settings.Language) || settings.Language == Core.Constants.Languages.Auto || GigyaLanguageHelper.Languages.ContainsKey(settings.Language) ? settings.Language : Core.Constants.Languages.Other;
            
            var model = new GigyaSettingsModel
            {
                CanViewApplicationSecret = (UmbracoUser.UserType.Name == Constants.UserTypes.Admin) || User.IsInRole(Constants.Roles.GigyaAdmin),
                Id = Convert.ToInt32(settings.Id),
                ApiKey = settings.ApiKey,
                ApplicationKey = settings.ApplicationKey,
                Language = language,
                LanguageFallback = new GigyaLanguageModel { Code = settings.LanguageFallback },
                LanguageOther = language.Code == Core.Constants.Languages.Other ? settings.Language : string.Empty,
                DebugMode = settings.DebugMode,
                DataCenter = string.IsNullOrEmpty(settings.DataCenter) || Core.Constants.DataCenter.DataCenters.Contains(settings.DataCenter) ? settings.DataCenter :  Core.Constants.DataCenter.Other,
                DataCenterOther = settings.DataCenter,
                EnableRaas = settings.EnableRaas,
                RedirectUrl = settings.RedirectUrl,
                LogoutUrl = settings.LogoutUrl,
                GlobalParameters = settings.GlobalParameters,
                SessionTimeout = settings.SessionTimeout
            };

            var mappingFields = !string.IsNullOrEmpty(settings.MappingFields) ? JsonConvert.DeserializeObject<List<MappingField>>(settings.MappingFields) : new List<MappingField>();
            AddMappingField(Constants.GigyaFields.Email, Constants.CmsFields.Email, ref mappingFields, true);
            AddMappingField(Constants.GigyaFields.FirstName, Constants.CmsFields.Name, ref mappingFields, true);
            AddMappingField(Constants.GigyaFields.UserId, Constants.CmsFields.Username, ref mappingFields, true);

            // required fields first
            model.MappingFields = mappingFields.OrderByDescending(i => i.Required).ThenBy(i => i.CmsFieldName).ToList();

            // check if authorised to view application secret
            if (model.CanViewApplicationSecret && !string.IsNullOrEmpty(settings.ApplicationSecret) && Encryptor.IsConfigured)
            {
                var key = Encryptor.Decrypt(settings.ApplicationSecret);
                if (!string.IsNullOrEmpty(key))
                {
                    model.ApplicationSecretMasked = StringHelper.MaskInput(key, "*", 2, 2);
                }
            }

            return model;
        }

        private void AddMappingField(string gigyaFieldName, string defaultUmbracoFieldName, ref List<MappingField> fields, bool required)
        {
            var field = fields.FirstOrDefault(i => i.CmsFieldName == defaultUmbracoFieldName);
            if (field != null)
            {
                field.Required = required;
            }
            else
            {
                fields.Add(new MappingField
                {
                    GigyaFieldName = gigyaFieldName,
                    CmsFieldName = defaultUmbracoFieldName,
                    Required = required
                });
            }
        }
    }
}