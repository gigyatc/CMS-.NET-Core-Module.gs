﻿using Gigya.Module.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Security;
using Telerik.Sitefinity.Security;
using Telerik.Sitefinity.Security.Model;
using Telerik.Sitefinity.Model;
using Telerik.Sitefinity.Data;
using System.Dynamic;
using Telerik.Sitefinity.Services;
using Gigya.Module.Connector.Events;
using Gigya.Module.Core.Connector.Helpers;
using Gigya.Module.Core.Connector.Logging;
using Gigya.Module.Core.Data;
using Gigya.Module.Core.Mvc.Models;
using Gigya.Module.Core.Connector.Common;
using Gigya.Module.Core.Connector.Models;
using Gigya.Socialize.SDK;
using Telerik.Sitefinity.Security.Claims;

namespace Gigya.Module.Connector.Helpers
{
    public class GigyaMembershipHelper : GigyaMembershipHelperBase, IGigyaMembershipHelper
    {
        public GigyaMembershipHelper(GigyaApiHelper apiHelper, Logger logger) : base(apiHelper, logger)
        {
        }

        protected override string CmsUserIdField => Constants.SitefinityFields.UserId;

        /// <summary>
        /// Updates the users Sitefinity profile.
        /// </summary>
        /// <param name="username">Id of the user to update.</param>
        /// <param name="settings">Gigya module settings for this site.</param>
        /// <param name="gigyaModel">Deserialized Gigya JSON object.</param>
        protected virtual bool MapProfileFieldsAndUpdate(string currentUsername, string updatedUsername, IGigyaModuleSettings settings, dynamic gigyaModel, List<MappingField> mappingFields)
        {
            UserProfileManager profileManager = UserProfileManager.GetManager();
            UserManager userManager = UserManager.GetManager();

            using (new ElevatedModeRegion(userManager))
            {
                var user = userManager.GetUser(currentUsername);
                user.Email = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.Email, Constants.GigyaFields.Email, mappingFields);

                if (user.UserName != updatedUsername)
                {
                    user.SetUserName(updatedUsername);
                }

                SitefinityProfile profile = profileManager.GetUserProfile<SitefinityProfile>(user);
                
                if (profile == null)
                {
                    profile = profileManager.CreateProfile(user, Guid.NewGuid(), typeof(SitefinityProfile)) as SitefinityProfile;

                    // only set this on creation as it's possible to get 2 users with the same email address
                    profile.Nickname = user.Email;
                }               

                // map any custom fields
                MapProfileFields(profile, gigyaModel, settings, mappingFields);
                
                try
                {
                    userManager.SaveChanges();
                    profileManager.SaveChanges();
                    return true;
                }
                catch(Exception e)
                {
                    _logger.Error("Failed to update profile for userId: " + currentUsername, e);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets a Gigya value from the model.
        /// </summary>
        /// <param name="gigyaModel">Deserialized Gigya JSON object.</param>
        /// <param name="gigyaFieldName">Gigya field name e.g. profile.age</param>
        /// <returns></returns>
        protected virtual object GetGigyaValue(dynamic gigyaModel, string gigyaFieldName, string sitefinityFieldName)
        {
            var eventArgs = new MapGigyaFieldEvent
            {
                GigyaModel = gigyaModel,
                GigyaFieldName = gigyaFieldName,
                SitefinityFieldName = sitefinityFieldName,
                Origin = "GetGigyaValue",
                GigyaValue = DynamicUtils.GetValue<object>(gigyaModel, gigyaFieldName)
            };
            EventHub.Raise(eventArgs);

            return eventArgs.GigyaValue;
        }

        /// <summary>
        /// Maps Gigya fields to a Sitefinity profile.
        /// </summary>
        /// <param name="profile">The profile to update.</param>
        /// <param name="gigyaModel">Deserialized Gigya JSON object.</param>
        /// <param name="settings">The Gigya module settings.</param>
        protected virtual void MapProfileFields(SitefinityProfile profile, dynamic gigyaModel, IGigyaModuleSettings settings, List<MappingField> mappingFields)
        {
			if (mappingFields == null)
            {
                return;
            }

            profile.FirstName = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.FirstName, Constants.GigyaFields.FirstName, mappingFields);
            profile.LastName = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.LastName, Constants.GigyaFields.LastName, mappingFields);

            // map custom fields
            foreach (var field in mappingFields)
            {
                // check if field is a custom one
                switch (field.CmsFieldName)
                {
                    case Constants.SitefinityFields.UserId:
                    case Constants.SitefinityFields.FirstName:
                    case Constants.SitefinityFields.LastName:
                    case Constants.SitefinityFields.Email:
                        continue;
                }

                if (!string.IsNullOrEmpty(field.CmsFieldName) && profile.DoesFieldExist(field.CmsFieldName))
                {
                    object gigyaValue = GetGigyaValue(gigyaModel, field.GigyaFieldName, field.CmsFieldName);
                    if (gigyaValue != null)
                    {
                        try
                        {
                            profile.SetValue(field.CmsFieldName, gigyaValue);
                        }
                        catch (Exception e)
                        {
                            _logger.Error(string.Format("Couldn't set Sitefinity profile value for [{0}] and gigya field [{1}].", field.CmsFieldName, field.GigyaFieldName), e);
                        }

                        var profileValue = profile.GetValue(field.CmsFieldName);
                        if (profileValue == null || profileValue.ToString() != gigyaValue.ToString())
                        {
                            _logger.Error(string.Format("Sitefinity field [{0}] type doesn't match Gigya field [{1}] type. You may need to add a conversion using EventHub.Subscribe<IMapGigyaFieldEvent>", field.CmsFieldName, field.GigyaFieldName));
                        }
                    }
                    else if (settings.DebugMode)
                    {
                        _logger.DebugFormat("Gigya field \"{0}\" is null so profile field not updated.", field.GigyaFieldName);
                    }
                }
                else if (settings.DebugMode)
                {
                    _logger.DebugFormat("Sitefinity field \"{0}\" not found.", field.CmsFieldName);
                }
            }
        }

        /// <summary>
        /// Creates a new Sitefinity user from a Gigya user.
        /// </summary>
        /// <param name="username">The Id of the new user.</param>
        /// <param name="gigyaModel">Deserialized Gigya JSON object.</param>
        /// <param name="settings">Gigya module settings for the site.</param>
        /// <returns></returns>
        protected virtual MembershipCreateStatus CreateUser(string username, dynamic gigyaModel, IGigyaModuleSettings settings, List<MappingField> mappingFields)
        {
            UserManager userManager = UserManager.GetManager();
            UserProfileManager profileManager = UserProfileManager.GetManager();

            MembershipCreateStatus status;
            var email = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.Email, Constants.GigyaFields.Email, mappingFields);
            var firstName = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.FirstName, Constants.GigyaFields.FirstName, mappingFields);
            var lastName = GetMappedFieldWithFallback(gigyaModel, Constants.SitefinityFields.LastName, Constants.GigyaFields.LastName, mappingFields);

            User user = userManager.CreateUser(username, Guid.NewGuid().ToString(), email, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), true, null, out status);
            SitefinityProfile profile = null;

            switch (status)
            {
                case MembershipCreateStatus.Success:
                    profile = CreateProfile(gigyaModel, settings, userManager, profileManager, user, mappingFields);
                    break;
                case MembershipCreateStatus.DuplicateEmail:
                    // insert fails if there is a duplicate email even though duplicate emails are allowed...strange huh
                    var dummyEmail = string.Concat(Guid.NewGuid(), "@gigya.com");

                    // try insert again with a dummy email and update it afterwards
                    user = userManager.CreateUser(username, Guid.NewGuid().ToString(), dummyEmail, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), true, null, out status);
                    if (status == MembershipCreateStatus.Success)
                    {
                        user.Email = email;
                        profile = CreateProfile(gigyaModel, settings, userManager, profileManager, user, mappingFields);
                    }
                    break;
            }

            if (user == null)
            {
                return status;
            }

            MapProfileFields(profile, gigyaModel, settings, mappingFields);

            try
            {
                userManager.SaveChanges();
                profileManager.SaveChanges();
            }
            catch (Exception e)
            {
                _logger.Error("Failed to update profile for userId: " + username, e);
            }

            return status;
        }

        /// <summary>
        /// Creates a new Sitefinity profile.
        /// </summary>
        /// <param name="gigyaModel">Deserialized Gigya JSON object.</param>
        /// <param name="settings">Gigya module settings.</param>
        /// <param name="userManager">Sitefinity user manager.</param>
        /// <param name="profileManager">Sitefinity profile manager.</param>
        /// <param name="user">The user that will be associated with the new profile.</param>
        protected virtual SitefinityProfile CreateProfile(dynamic gigyaModel, IGigyaModuleSettings settings, UserManager userManager, UserProfileManager profileManager, User user, List<MappingField> mappingFields)
        {
            SitefinityProfile profile = profileManager.CreateProfile(user, Guid.NewGuid(), typeof(SitefinityProfile)) as SitefinityProfile;

            if (profile != null)
            {
                profile.Nickname = user.Email;

                userManager.SaveChanges();
                profileManager.RecompileItemUrls(profile);
                profileManager.SaveChanges();

                MapProfileFields(profile, gigyaModel, settings, mappingFields);
            }

            try
            {
                profileManager.RecompileItemUrls(profile);
                profileManager.SaveChanges();
            }
            catch (Exception e)
            {
                _logger.Error("Failed to create profile for userId: " + user.Id, e);
            }

            return profile;
        }

        /// <summary>
        /// Login or register a user.
        /// </summary>
        /// <param name="model">Details from the client e.g. signature and userId.</param>
        /// <param name="settings">Gigya module settings.</param>
        /// <param name="response">Response model that will be returned to the client.</param>
        public virtual void LoginOrRegister(LoginModel model, IGigyaModuleSettings settings, ref LoginResponseModel response)
        {
            response.Status = ResponseStatus.Error;
            
            if (!settings.EnableRaas)
            {
                if (settings.DebugMode)
                {
                    _logger.Debug("RaaS not enabled so login aborted.");
                }
                return;
            }
            
            if (!_gigyaApiHelper.ValidateApplicationKeySignature(model.UserId, settings, model.SignatureTimestamp, model.Signature))
            {
                if (settings.DebugMode)
                {
                    _logger.Debug("Invalid user signature for login request.");
                }
                return;
            }

            // get user info
            var userInfoResponse = _gigyaApiHelper.GetAccountInfo(model.UserId, settings);
            if (userInfoResponse == null || userInfoResponse.GetErrorCode() != 0)
            {
                _logger.Error("Failed to getAccountInfo");
                return;
            }
            
			List<MappingField> mappingFields = GetMappingFields(settings);
			var gigyaModel = GetAccountInfo(model.Id, settings, userInfoResponse, mappingFields);
            ThrowTestingExceptionIfRequired(settings, gigyaModel);

            // find what field has been configured for the CMS username
            var username = GetCmsUsername(mappingFields, gigyaModel);

            UserManager manager = UserManager.GetManager();
            var userExists = manager.UserExists(username);
            if (!userExists)
            {
                // user doesn't exist so create a new one
                using (new ElevatedModeRegion(manager))
                {
                    var createUserStatus = CreateUser(username, gigyaModel, settings, mappingFields);
                    if (createUserStatus != MembershipCreateStatus.Success)
                    {
                        return;
                    }
                }
            }

            // user logged into Gigya so now needs to be logged into Sitefinity
            var authenticated = AuthenticateUser(username, settings, userExists, gigyaModel, mappingFields);
            response.Status = authenticated ? ResponseStatus.Success : ResponseStatus.Error;
            if (authenticated)
            {
                response.RedirectUrl = settings.RedirectUrl;

                var settingsHelper = new GigyaSettingsHelper();
                var accountHelper = new GigyaAccountHelper(settingsHelper, _logger, this, settings);
                accountHelper.UpdateSessionExpirationCookieIfRequired(HttpContext.Current, true);
            }
        }

        /// <summary>
        /// Updates a user's profile in Sitefinity.
        /// </summary>
        public virtual void UpdateProfile(LoginModel model, IGigyaModuleSettings settings, ref LoginResponseModel response)
        {
            var userInfoResponse = ValidateRequest(model, settings);
            if (userInfoResponse == null)
            {
                return;
            }

            var currentIdentity = ClaimsManager.GetCurrentIdentity();
            var currentUsername = currentIdentity.Name;

            List<MappingField> mappingFields = GetMappingFields(settings);

			var gigyaModel = GetAccountInfo(model.Id, settings, userInfoResponse, mappingFields);
            ThrowTestingExceptionIfRequired(settings, gigyaModel);

            string username = GetCmsUsername(mappingFields, gigyaModel);
            var success = MapProfileFieldsAndUpdate(currentUsername, username, settings, gigyaModel, mappingFields);
            if (success)
            {
                response.RedirectUrl = settings.RedirectUrl;
                response.Status = ResponseStatus.Success;

                if (currentUsername != username)
                {
                    AuthenticateUser(username, settings, false, gigyaModel, mappingFields);
                }
            }
        }

		/// <summary>
        /// In the Sitefinity module the current site id is passed as an array so we need to convert to an int.
        /// </summary>
        protected override object ConvertCurrentSiteId(object currentSiteId)
        {
            var idList = currentSiteId as string[];
            if (idList != null)
            {
                currentSiteId = idList[0];
            }

            return Guid.Parse(currentSiteId.ToString());
        }

        /// <summary>
        /// Gets the Gigya UID for the current logged in user. If the user isn't logged in, null is returned.
        /// </summary>
        /// <param name="settings">Current site settings.</param>
        /// <returns>The UID value.</returns>
        public string GetUidForCurrentUser(IGigyaModuleSettings settings)
        {
            // check user is logged in
            var currentIdentity = ClaimsManager.GetCurrentIdentity();
            if (!currentIdentity.IsAuthenticated)
            {
                return null;
            }

            // get mapping field - hopefully the Sitefinity username field maps to Gigya's UID
            var field = settings.MappedMappingFields.FirstOrDefault(i => i.GigyaFieldName == Constants.GigyaFields.UserId);
            if (field == null || field.CmsFieldName == Constants.SitefinityFields.UserId)
            {
                return currentIdentity.Name;
            }

            // get UID field from profile
            UserProfileManager profileManager = UserProfileManager.GetManager();
            UserManager userManager = UserManager.GetManager();

            var user = userManager.GetUser(currentIdentity.Name);
            if (user == null)
            {
                return currentIdentity.Name;
            }
            
            var profile = profileManager.GetUserProfile<SitefinityProfile>(user);
            if (profile == null)
            {
                return currentIdentity.Name;
            }

            return profile.GetValue<string>(field.CmsFieldName);
        }

        /// <summary>
        /// Authenticates a user in Sitefinity.
        /// </summary>
        protected virtual bool AuthenticateUser(string username, IGigyaModuleSettings settings, bool updateProfile, dynamic gigyaModel, List<MappingField> mappingFields)
        {
            User user;

            var persistent = PersistentAuthRequired(settings);
            var loginStatus = AuthenticateWithRetry(username, 0, 2, persistent, out user);
            switch (loginStatus)
            {
                case UserLoggingReason.Success:
                    if (settings.DebugMode)
                    {
                        _logger.Debug(string.Concat("User [", username, "] successfully logged into Sitefinity."));
                    }

                    if (updateProfile)
                    {
                        MapProfileFieldsAndUpdate(username, username, settings, gigyaModel, mappingFields);
                    }
                    return true;
                default:
                    _logger.Error(string.Format("User [{0}] not logged into Sitefinity. Reason: {1}.", username, loginStatus));
                    return false;
            }
        }

        /// <summary>
        /// Authenticates user with the ability to retry if an attempt fails.   The cookie created is a session cookie.
        /// </summary>
        /// <param name="userId">UserId to authenticate.</param>
        /// <param name="attempts">Number to attempts so far.</param>
        /// <param name="retries">Number of retries allowed.</param>
        /// <param name="user">The authenticated user.</param>
        /// <returns></returns>
        [Obsolete]
        protected virtual UserLoggingReason AuthenticateWithRetry(string userId, int attempts, int retries, out User user)
        {
            return AuthenticateWithRetry(userId, attempts, retries, false, out user);
        }

        /// <summary>
        /// Authenticates user with the ability to retry if an attempt fails.
        /// </summary>
        /// <param name="userId">UserId to authenticate.</param>
        /// <param name="attempts">Number to attempts so far.</param>
        /// <param name="retries">Number of retries allowed.</param>
        /// <param name="user">The authenticated user.</param>
        /// <returns></returns>
        protected virtual UserLoggingReason AuthenticateWithRetry(string userId, int attempts, int retries, bool persistent, out User user)
        {
            attempts++;

            var loginStatus = SecurityManager.AuthenticateUser(null, userId, persistent, out user);
            switch (loginStatus)
            {
                case UserLoggingReason.Success:
                    return loginStatus;
                default:
                    if (attempts < retries)
                    {
                        return AuthenticateWithRetry(userId, attempts, retries, persistent, out user);
                    }
                    return loginStatus;
            }
        }

        protected override bool LoginByUsername(string username, IGigyaModuleSettings settings)
        {
            var persistent = PersistentAuthRequired(settings);
            User user;
            var loginStatus = SecurityManager.AuthenticateUser(null, username, persistent, out user);
            return loginStatus == UserLoggingReason.Success;
        }

        public void Logout()
        {
            SecurityManager.Logout();
        }
    }
}