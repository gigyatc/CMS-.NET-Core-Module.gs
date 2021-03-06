﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Gigya.Module.Core.Data;
using Gigya.Module.Core.Connector.Encryption;
using System.Reflection;
using System.Diagnostics;
using System.Globalization;
using Gigya.Module.Core.Mvc.Models;
using Newtonsoft.Json;
using System.Dynamic;
using Gigya.Module.Core.Connector.Common;

using System.Web.Mvc;
using System.Configuration;
using Gigya.Umbraco.Module.v621.Data;
using Umbraco.Web;
using Gigya.Umbraco.Module.v621.Connector;
using Gigya.Module.Core.Connector.Helpers;

namespace Gigya.Umbraco.Module.v621.Helpers
{
    public class GigyaSettingsHelper : Gigya.Module.Core.Connector.Helpers.GigyaSettingsHelper
    {
        public static readonly string _CmsVersion = ConfigurationManager.AppSettings["umbracoConfigurationStatus"];

        public override string CmsName
        {
            get
            {
                return "Umbraco";
            }
        }

        public override string CmsVersion
        {
            get
            {
                return _CmsVersion;
            }
        }

        public override string ModuleVersion
        {
            get
            {
                return Constants.ModuleVersion;
            }
        }

        public override IGigyaModuleSettings GetForCurrentSite(bool decrypt = false)
        {
            // get current page id
            var currentPageId = UmbracoContext.Current.PageId;
            if (!currentPageId.HasValue)
            {
                throw new ArgumentException("No current page Id");
            }

            var umbracoHelper = new UmbracoHelper(UmbracoContext.Current);
            var currentNode = umbracoHelper.TypedContent(currentPageId);

            // find homepage from current node
            var homepage = Utils.HomepageNode(currentNode);
            
            var model = Get(homepage.Id, decrypt);

            // if we are using global settings we still want to tell the client to use the current homepage id
            model.Id = homepage.Id;
            return model;
        }

        public GigyaUmbracoModuleSettings GetRaw(int id)
        {
            var db = UmbracoContext.Current.Application.DatabaseContext.Database;
            return db.SingleOrDefault<GigyaUmbracoModuleSettings>(id) ?? new GigyaUmbracoModuleSettings { Id = id, IsNew = true };
        }

        public void Save(GigyaUmbracoModuleSettings settings)
        {
            var db = UmbracoContext.Current.Application.DatabaseContext.Database;

            if (settings.IsNew)
            {
                db.Insert(settings);
            }
            else
            {
                db.Save(settings);
            }
        }

        public override void Delete(object id)
        {
            var db = UmbracoContext.Current.Application.DatabaseContext.Database;
            db.Delete<GigyaUmbracoModuleSettings>(id);
        }

        protected override IGigyaModuleSettings EmptySettings(object id)
        {
            return new GigyaModuleSettings { Id = id, DebugMode = true, EnableRaas = true };
        }

        protected override List<IGigyaModuleSettings> GetForSiteAndDefault(object id)
        {
            if (id == null)
            {
                id = -1;
            }

            var idList = id as string[];
            if (idList != null)
            {
                id = idList[0];
            }
            
            var db = UmbracoContext.Current.Application.DatabaseContext.Database;
            var sql = string.Format("SELECT * FROM gigya_settings WHERE Id IN ({0}, -1)", id);
            var data = db.Fetch<GigyaUmbracoModuleSettings>(sql);
            return data.Select(Map).ToList();
        }

        protected override string Language(IGigyaModuleSettings settings)
        {
            var languageHelper = new GigyaLanguageHelper();
            return languageHelper.Language(settings, CultureInfo.CurrentUICulture);
        }

        protected override string ClientScriptPath(IGigyaModuleSettings settings, UrlHelper urlHelper)
        {
            var scriptName = settings.DebugMode ? "gigya-cms.js" : "gigya-cms.min.js";
            return string.Concat("~/scripts/", scriptName);
        }

        private IGigyaModuleSettings Map(GigyaUmbracoModuleSettings settings)
        {
            return new GigyaModuleSettings
            {
                Id = settings.Id,
                ApiKey = settings.ApiKey,
                ApplicationKey = settings.ApplicationKey,
                ApplicationSecret = settings.ApplicationSecret,
                Language = settings.Language,
                LanguageFallback = settings.LanguageFallback,
                DebugMode = settings.DebugMode,
                DataCenter = settings.DataCenter,
                EnableRaas = settings.EnableRaas,
                RedirectUrl = settings.RedirectUrl,
                LogoutUrl = settings.LogoutUrl,
                MappingFields = settings.MappingFields,
                GlobalParameters = settings.GlobalParameters,
                SessionTimeout = settings.SessionTimeout
            };
        }
    }
}
