﻿<%@ Control Language="C#" %>
<%@ Register Assembly="Telerik.Sitefinity" Namespace="Telerik.Sitefinity.Web.UI.Fields" TagPrefix="sfFields" %>
<%@ Register Assembly="Telerik.Sitefinity" Namespace="Telerik.Sitefinity.Web.UI" TagPrefix="sf" %>
<%@ Register Assembly="Telerik.Sitefinity" Namespace="Telerik.Sitefinity.Web.UI.FieldControls" TagPrefix="sfFields" %>

<style>
    .sfTxt { margin-bottom: 10px; }
    .mapping-fields-table td { padding: 5px 0 5px 5px; vertical-align: middle; }
    .mapping-fields-table th { padding-left: 5px; }
    .mapping-fields-table tr td:first-child, .mapping-fields-table tr th:first-child { padding-left: 0; }
    .add-mapping-field { display: inline-block; margin-bottom: 10px; }
    .mapping-fields-table .sfTxt { width: auto; margin: 0; }
    .sfDropdownList { display: inline-block; margin-bottom: 10px; }
    .required { color: red; }
    select { padding: 2px 2px 3px 2px; }
</style>

<div class="sfSettingsSection">
    <h2>
        <asp:Literal runat="server" ID="lGeneralSettings" Text="Gigya DS Settings" />
    </h2>
    <p>Settings will be verified before being saved to the database.</p>
</div>

<sf:ResourceLinks id="resourcesLinks1" runat="server">
	<sf:EmbeddedResourcePropertySetter Name="Telerik.Sitefinity.Resources.Themes.Default.Images.Loadings.sfLoadingFormBtns.gif"
		Static="true" ControlID="GigyaLoadingImage" ControlPropertyName="ImageUrl" />
</sf:ResourceLinks>

<div id="loading-view" class="sfLoadingFormBtns sfButtonArea">
	<sf:SfImage ID="GigyaLoadingImage" runat="server" AlternateText="<%$Resources:Labels, SavingImgAlt %>" />
</div>

<div id="gigya-settings-section" style="display: none;" class="sfSettingsSection">
    <div class="sfTxtLbl sfInlineBlock">
        Method<span class='required'>*</span>
    </div>
    <sfFields:ChoiceField ID="Method" Title="" runat="server" DataFieldName="Method" DisplayMode="Write" RenderChoicesAs="RadioButtons">
        <Choices>
            <sfFields:ChoiceItem Text="Search (Recommended when using more than one DS type or OID)" Value="0" />
            <sfFields:ChoiceItem Text="Get (Recommended when using one DS type and one OID)" Value="1" />
        </Choices>
    </sfFields:ChoiceField>
    <div id="profile-properties-wrapper" style="display: none;">
        <sfFields:TextField runat="server"
            DataFieldName="ProfileProperties"
            Title="Profile Properties"
            DisplayMode="Write"
            WrapperTag="div">
        </sfFields:TextField>
    </div>
    <div id="mapping-field-wrapper" style="display: none;">
        <sfFields:TextField ID="MappingFieldsField" runat="server"
            DataFieldName="MappingFields"
            Title="Mapping Fields"
            DisplayMode="Write"
            WrapperTag="div">
        </sfFields:TextField>
    </div>
    <br />
    <div class="sfTxtLbl sfInlineBlock">
        Fields Mapping
    </div>
    <table class="mapping-fields-table">
        <thead>
            <tr>
                <th>Gigya DS Field</th>
                <th>Gigya DS OID</th>
                <th>Sitefinity Field</th>
                <th></th>
            </tr>
        </thead>
        <tbody>
            <tr style="display: none;">
                <td>
                    <input class="gigya-mapping-field sfTxt" placeholder="ds.userInfo.age_i" type="text" />
                </td>
                <td>
                    <input class="gigya-ds-oid sfTxt" type="text" />
                </td>
                <td>
                    <input class="sf-mapping-field sfTxt" type="text" />
                    <select class="sf-mapping-field"></select>
                </td>
                <td>
                    <a href="#" class="remove">Remove</a>
                </td>
            </tr>
        </tbody>
    </table>
    <a href="#" class="add-mapping-field">Add new mapping field</a>

    <div style="display: none;" id="loaded-field-wrapper">
        <sfFields:TextField runat="server"
            DataFieldName="LoadedField"
            DisplayMode="Write"
            WrapperTag="div">
        </sfFields:TextField>
    </div>
</div>

<script>
    var gigyaSettings = {
        mappingTable: null,
        hiddenMappingField: null,
        init: function () {
            $('#gigya-settings-section :text').attr('autocomplete', 'off');

            gigyaSettings.mappingTable = $('.mapping-fields-table');
            gigyaSettings.hiddenMappingField = $('#mapping-field-wrapper input');

            $('.add-mapping-field').click(function () {
                gigyaSettings.addMappingRow('', '', '');
                return false;
            });

            gigyaSettings.mappingTable.on('change', 'select', function () {
                gigyaSettings.updateFieldMappings();
            });

            gigyaSettings.mappingTable.on('blur', 'input', function () {
                gigyaSettings.updateFieldMappings();
            }).on('click', '.remove', function () {
                if (gigyaSettings.mappingTable.find('tbody tr').length > 1) {
                    $(this).closest('tr').remove();
                    gigyaSettings.updateFieldMappings();
                }
                return false;
            });

            gigyaSettings.addProfileProperties();
            gigyaSettings.deserializeFieldMappings();

            $('.sfInheritanceMsg .sfLinkBtn').click(function () {
                setTimeout(function () {
                    gigyaSettings.toggleDisabledFields(!$('#mapping-field-wrapper input').is(':disabled'));
                }, 100);
            });
        },
        addMappingRow: function(gigyaValue, sitefinityValue, oid) {
            var firstRow = gigyaSettings.mappingTable.find('tbody tr:first');
            var newRow = firstRow.clone().show();

            var gigyaField = newRow.find('.gigya-mapping-field').val(gigyaValue);
            newRow.find('.remove').show();

            newRow.find('.gigya-ds-oid').val(oid);
            newRow.find('.sf-mapping-field').val(sitefinityValue);
            gigyaSettings.mappingTable.find('tbody').append(newRow);
        },
        addProfileProperties: function () {
            var profileSelect = $('.mapping-fields-table select.sf-mapping-field');
            var profileProperties = $('#profile-properties-wrapper :text').val();
            if (profileProperties.length == 0) {
                profileSelect.remove();
            } else {
                var profilePropertiesMapped = $.parseJSON(profileProperties);
                for (var i = 0; i < profilePropertiesMapped.length; i++) {
                    profileSelect.append(
                        $('<option />')
                        .text(profilePropertiesMapped[i].Value)
                        .val(profilePropertiesMapped[i].Key)
                    );
                }
                $('.mapping-fields-table input.sf-mapping-field').remove();
            }
        },
        deserializeFieldMappings: function() {
            var mappingsRaw = gigyaSettings.hiddenMappingField.val();
            if (mappingsRaw.length == 0) {
                return;
            }

            var mappings = $.parseJSON(mappingsRaw);
            for (var i = 0; i < mappings.length; i++) {
                var mapping = mappings[i];
                gigyaSettings.addMappingRow(mapping.gigyaFieldName, mapping.cmsFieldName, mapping.oid);
            }
        },
        updateFieldMappings: function () {
            var mappings = [];
            gigyaSettings.mappingTable.find('tbody tr:visible').each(function () {
                var $this = $(this);

                mappings.push({
                    gigyaFieldName: $this.find('.gigya-mapping-field').val(),
                    oid: $this.find('.gigya-ds-oid').val(),
                    cmsFieldName: $this.find('.sf-mapping-field').val()
                });
            });

            gigyaSettings.hiddenMappingField.val(JSON.stringify(mappings));
        },
        toggleDisabledFields: function (enabled) {
            if (enabled) {
                $('#gigya-settings-section input, #gigya-settings-section textarea, #gigya-settings-section select').prop('disabled', false);
                $('#gigya-settings-section .add-mapping-field, #gigya-settings-section .remove').show();
            } else {
                $('#gigya-settings-section input, #gigya-settings-section textarea, #gigya-settings-section select').prop('disabled', true);
                $('#gigya-settings-section .add-mapping-field, #gigya-settings-section .remove').hide();
            }
        }
    };

    // poll to check if user allowed to see fields - I can't find a 'sitefinity data service complete' event
    window.loadedTimeout = setInterval(function () {
        if ($('#loaded-field-wrapper :text').val().length > 0) {
            clearInterval(window.loadedTimeout);

            gigyaSettings.init();

            if ($('#mapping-field-wrapper input').is(':disabled')) {
                gigyaSettings.toggleDisabledFields(false);
            }

            $('#loading-view').hide();
            $('#gigya-settings-section').fadeIn(1000);
        }
    }, 500);
</script>