﻿using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Infrastructure;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Web.Areas.Admin.Factories;
using Nop.Web.Areas.Admin.Models.Common;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nop.Web.Areas.Admin.Controllers
{
    public partial class CommonController : BaseAdminController
    {
        #region Const

        private const string EXPORT_IMPORT_PATH = @"files\exportimport";

        #endregion

        #region Fields

        private readonly ICommonModelFactory _commonModelFactory;
        private readonly ICustomerService _customerService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly ILanguageService _languageService;
        private readonly ILocalizationService _localizationService;
        private readonly IMaintenanceService _maintenanceService;
        private readonly INopFileProvider _fileProvider;
        private readonly IPermissionService _permissionService;
        private readonly IStaticCacheManager _cacheManager;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public CommonController(ICommonModelFactory commonModelFactory,
            ICustomerService customerService,
            IDateTimeHelper dateTimeHelper,
            ILanguageService languageService,
            ILocalizationService localizationService,
            IMaintenanceService maintenanceService,
            INopFileProvider fileProvider,
            IPermissionService permissionService,
            IStaticCacheManager cacheManager,
            IUrlRecordService urlRecordService,
            IWebHelper webHelper,
            IWorkContext workContext)
        {
            this._commonModelFactory = commonModelFactory;
            this._customerService = customerService;
            this._dateTimeHelper = dateTimeHelper;
            this._languageService = languageService;
            this._localizationService = localizationService;
            this._maintenanceService = maintenanceService;
            this._fileProvider = fileProvider;
            this._permissionService = permissionService;
            this._cacheManager = cacheManager;
            this._urlRecordService = urlRecordService;
            this._webHelper = webHelper;
            this._workContext = workContext;
        }

        #endregion

        #region Methods

        public virtual IActionResult SystemInfo()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //prepare model
            var model = _commonModelFactory.PrepareSystemInfoModel(new SystemInfoModel());

            return View(model);
        }

        public virtual IActionResult Warnings()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //prepare model
            var model = _commonModelFactory.PrepareSystemWarningModels();

            return View(model);
        }

        public virtual IActionResult Maintenance()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //prepare model
            var model = _commonModelFactory.PrepareMaintenanceModel(new MaintenanceModel());

            return View(model);
        }

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("delete-guests")]
        public virtual IActionResult MaintenanceDeleteGuests(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var startDateValue = model.DeleteGuests.StartDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteGuests.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            var endDateValue = model.DeleteGuests.EndDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteGuests.EndDate.Value, _dateTimeHelper.CurrentTimeZone).AddDays(1);

            model.DeleteGuests.NumberOfDeletedCustomers = _customerService.DeleteGuestCustomers(startDateValue, endDateValue);

            return View(model);
        }


        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("delete-exported-files")]
        public virtual IActionResult MaintenanceDeleteFiles(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var startDateValue = model.DeleteExportedFiles.StartDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteExportedFiles.StartDate.Value, _dateTimeHelper.CurrentTimeZone);

            var endDateValue = model.DeleteExportedFiles.EndDate == null ? null
                            : (DateTime?)_dateTimeHelper.ConvertToUtcTime(model.DeleteExportedFiles.EndDate.Value, _dateTimeHelper.CurrentTimeZone).AddDays(1);

            model.DeleteExportedFiles.NumberOfDeletedFiles = 0;

            foreach (var fullPath in _fileProvider.GetFiles(_fileProvider.GetAbsolutePath(EXPORT_IMPORT_PATH)))
            {
                try
                {
                    var fileName = _fileProvider.GetFileName(fullPath);
                    if (fileName.Equals("index.htm", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                    var info = _fileProvider.GetFileInfo(_fileProvider.Combine(EXPORT_IMPORT_PATH, fileName));
                    var lastModifiedTimeUtc = info.LastModified.UtcDateTime;
                    if ((!startDateValue.HasValue || startDateValue.Value < lastModifiedTimeUtc) &&
                        (!endDateValue.HasValue || lastModifiedTimeUtc < endDateValue.Value))
                    {
                        _fileProvider.DeleteFile(fullPath);
                        model.DeleteExportedFiles.NumberOfDeletedFiles++;
                    }
                }
                catch (Exception exc)
                {
                    ErrorNotification(exc, false);
                }
            }

            return View(model);
        }

        [HttpPost]
        public virtual IActionResult BackupFiles(BackupFileSearchModel searchModel)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedKendoGridJson();

            //prepare model
            var model = _commonModelFactory.PrepareBackupFileListModel(searchModel);

            return Json(model);
        }

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("backup-database")]
        public virtual IActionResult BackupDatabase(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            try
            {
                _maintenanceService.BackupDatabase();
                SuccessNotification(_localizationService.GetResource("Admin.System.Maintenance.BackupDatabase.BackupCreated"));
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
            }

            return View(model);
        }

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("re-index")]
        public virtual IActionResult ReIndexTables(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            try
            {
                _maintenanceService.ReIndexTables();
                SuccessNotification(_localizationService.GetResource("Admin.System.Maintenance.ReIndexTables.Complete"));
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
            }

            return View(model);
        }

        [HttpPost, ActionName("Maintenance")]
        [FormValueRequired("backupFileName", "action")]
        public virtual IActionResult BackupAction(MaintenanceModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            var action = Request.Form["action"];

            var fileName = Request.Form["backupFileName"];
            var backupPath = _maintenanceService.GetBackupPath(fileName);

            try
            {
                switch (action)
                {
                    case "delete-backup":
                        {
                            _fileProvider.DeleteFile(backupPath);
                            SuccessNotification(string.Format(_localizationService.GetResource("Admin.System.Maintenance.BackupDatabase.BackupDeleted"), fileName));
                        }
                        break;
                    case "restore-backup":
                        {
                            _maintenanceService.RestoreDatabase(backupPath);
                            SuccessNotification(_localizationService.GetResource("Admin.System.Maintenance.BackupDatabase.DatabaseRestored"));
                        }
                        break;
                }
            }
            catch (Exception exc)
            {
                ErrorNotification(exc);
            }

            return View(model);
        }

        public virtual IActionResult SetLanguage(int langid, string returnUrl = "")
        {
            var language = _languageService.GetLanguageById(langid);
            if (language != null)
                _workContext.WorkingLanguage = language;

            //home page
            if (string.IsNullOrEmpty(returnUrl))
                returnUrl = Url.Action("Index", "Home", new { area = AreaNames.Admin });

            //prevent open redirection attack
            if (!Url.IsLocalUrl(returnUrl))
                return RedirectToAction("Index", "Home", new { area = AreaNames.Admin });

            return Redirect(returnUrl);
        }

        [HttpPost]
        public virtual IActionResult ClearCache(string returnUrl = "")
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            _cacheManager.Clear();

            //home page
            if (string.IsNullOrEmpty(returnUrl))
                return RedirectToAction("Index", "Home", new { area = AreaNames.Admin });

            //prevent open redirection attack
            if (!Url.IsLocalUrl(returnUrl))
                return RedirectToAction("Index", "Home", new { area = AreaNames.Admin });

            return Redirect(returnUrl);
        }

        [HttpPost]
        public virtual IActionResult RestartApplication(string returnUrl = "")
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //restart application
            _webHelper.RestartAppDomain();

            //home page
            if (string.IsNullOrEmpty(returnUrl))
                return RedirectToAction("Index", "Home", new { area = AreaNames.Admin });

            //prevent open redirection attack
            if (!Url.IsLocalUrl(returnUrl))
                return RedirectToAction("Index", "Home", new { area = AreaNames.Admin });

            return Redirect(returnUrl);
        }

        public virtual IActionResult SeNames()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            //prepare model
            var model = _commonModelFactory.PrepareUrlRecordSearchModel(new UrlRecordSearchModel());

            return View(model);
        }

        [HttpPost]
        public virtual IActionResult SeNames(UrlRecordSearchModel searchModel)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedKendoGridJson();

            //prepare model
            var model = _commonModelFactory.PrepareUrlRecordListModel(searchModel);

            return Json(model);
        }

        [HttpPost]
        public virtual IActionResult DeleteSelectedSeNames(ICollection<int> selectedIds)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageMaintenance))
                return AccessDeniedView();

            if (selectedIds != null)
                _urlRecordService.DeleteUrlRecords(_urlRecordService.GetUrlRecordsByIds(selectedIds.ToArray()));

            return Json(new { Result = true });
        }

        [HttpPost]
        public virtual IActionResult PopularSearchTermsReport(PopularSearchTermSearchModel searchModel)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManageProducts))
                return AccessDeniedKendoGridJson();

            //prepare model
            var model = _commonModelFactory.PreparePopularSearchTermListModel(searchModel);

            return Json(model);
        }

        //action displaying notification (warning) to a store owner that entered SE URL already exists
        public virtual IActionResult UrlReservedWarning(string entityId, string entityName, string seName)
        {
            if (string.IsNullOrEmpty(seName))
                return Json(new { Result = string.Empty });

            int.TryParse(entityId, out var parsedEntityId);
            var validatedSeName = _urlRecordService.ValidateSeName(parsedEntityId, entityName, seName, null, false);

            if (seName.Equals(validatedSeName, StringComparison.InvariantCultureIgnoreCase))
                return Json(new { Result = string.Empty });

            return Json(new { Result = string.Format(_localizationService.GetResource("Admin.System.Warnings.URL.Reserved"), validatedSeName) });
        }

        #endregion
    }
}