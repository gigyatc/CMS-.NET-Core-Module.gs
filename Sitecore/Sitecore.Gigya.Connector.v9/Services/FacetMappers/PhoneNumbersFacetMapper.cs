﻿using Gigya.Module.Core.Connector.Common;
using Gigya.Module.Core.Connector.Logging;
using Sitecore.Analytics.Model.Entities;
using Sitecore.Analytics.Model.Framework;
using Sitecore.Gigya.Extensions.Abstractions.Analytics.Models;
using Sitecore.Gigya.Extensions.Abstractions.Services;
using Sitecore.Gigya.Connector.Providers;
using Sitecore.XConnect.Collection.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Sitecore.Gigya.Connector.Services.FacetMappers
{
    public class PhoneNumbersFacetMapper : FacetMapperBase<ContactPhoneNumbersMapping>
    {
        public PhoneNumbersFacetMapper(IContactProfileProvider contactProfileProvider, Logger logger) : base(contactProfileProvider, logger)
        {
        }

        protected override void UpdateFacet(dynamic gigyaModel, ContactPhoneNumbersMapping mapping)
        {
            if (mapping == null || mapping.Entries == null || !mapping.Entries.Any())
            {
                return;
            }

            try
            {
                var facet = _contactProfileProvider.PhoneNumbers;

                for (int i = 0; i < mapping.Entries.Count; i++)
                {
                    var mappingEntry = mapping.Entries[i];
                    if (string.IsNullOrEmpty(mappingEntry.Key))
                    {
                        _logger.Error("Key for phone number mapping is empty.");
                        continue;
                    }

                    var model = Map(gigyaModel, mappingEntry);

                    if (i == 0)
                    {
                        if (facet == null)
                        {
                            facet = new PhoneNumberList(model, mappingEntry.Key);
                        }
                        else
                        {
                            facet.PreferredPhoneNumber = model;
                            facet.PreferredKey = mappingEntry.Key;
                        }
                    }
                    else
                    {
                        facet.Others.Add(mappingEntry.Key, model);
                    }
                }

                _contactProfileProvider.SetFacet(facet, PhoneNumberList.DefaultFacetKey);
            }
            catch (FacetNotAvailableException ex)
            {
                _logger.Warn("The 'Phone Numbers' facet is not available.", ex);
            }
        }

        private PhoneNumber Map(dynamic gigyaModel, ContactPhoneNumberMapping entryMapping)
        {
            var countryCode = DynamicUtils.GetValue<string>(gigyaModel, entryMapping.CountryCode);
            var number = DynamicUtils.GetValue<string>(gigyaModel, entryMapping.Number);
            var entry = new PhoneNumber(countryCode, number);
            return entry;
        }
    }
}