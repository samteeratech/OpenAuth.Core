﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Infrastructure;
using Infrastructure.Helpers;
using Infrastructure.Utilities;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Options;
using OpenAuth.App.Interface;
using OpenAuth.App.Request;
using OpenAuth.App.Response;
using OpenAuth.Repository.Core;
using OpenAuth.Repository.Domain;
using OpenAuth.Repository.Interface;


namespace OpenAuth.App
{
    public class BuilderTableApp : BaseApp<BuilderTable>
    {
        private BuilderTableColumnApp _builderTableColumnApp;
        private DbExtension _dbExtension;
        private string webProject = null;
        private string apiNameSpace = null;
        private string startName = "";
        private IOptions<AppSetting> _appConfiguration;

        public BuilderTableApp(IUnitWork unitWork, IRepository<BuilderTable> repository,
            RevelanceManagerApp app, IAuth auth, DbExtension dbExtension, BuilderTableColumnApp builderTableColumnApp,
            IOptions<AppSetting> appConfiguration) : base(unitWork, repository, auth)
        {
            _dbExtension = dbExtension;
            _builderTableColumnApp = builderTableColumnApp;
            _appConfiguration = appConfiguration;
        }

        private string StratName
        {
            get
            {
                if (startName == "")
                {
                    startName = WebProject.Substring(0, webProject.IndexOf('.'));
                }

                return startName;
            }
        }

        private string WebProject
        {
            get
            {
                if (webProject != null)
                    return webProject;
                webProject = ProjectPath.GetLastIndexOfDirectoryName(".WebApi") ??
                             ProjectPath.GetLastIndexOfDirectoryName("Api") ??
                             ProjectPath.GetLastIndexOfDirectoryName(".Mvc");
                if (webProject == null)
                {
                    throw new Exception("未获取到以.WebApi结尾的项目名称,无法创建页面");
                }

                return webProject;
            }
        }

        /// <summary>
        /// 加载列表
        /// </summary>
        public TableData Load(QueryBuilderTableListReq request)
        {
            var loginContext = _auth.GetCurrentUser();
            if (loginContext == null)
            {
                throw new CommonException("登录已过期", Define.INVALID_TOKEN);
            }

            var result = new TableData();
            var objs = UnitWork.Find<BuilderTable>(null);
            if (!string.IsNullOrEmpty(request.key))
            {
                objs = objs.Where(u => u.Id.Contains(request.key));
            }

            result.data = objs.OrderBy(u => u.Id)
                .Skip((request.page - 1) * request.limit)
                .Take(request.limit);
            result.count = objs.Count();
            return result;
        }

        public string Add(AddOrUpdateBuilderTableReq req)
        {
            var columns = _dbExtension.GetDbTableStructure(req.TableName);
            if (!columns.Any())
            {
                throw new Exception($"未能找到{req.TableName}表结构定义");
            }

            var obj = req.MapTo<BuilderTable>();
            //todo:补充或调整自己需要的字段
            obj.CreateTime = DateTime.Now;
            var user = _auth.GetCurrentUser().User;
            obj.CreateUserId = user.Id;
            obj.CreateUserName = user.Name;
            UnitWork.Add(obj);

            foreach (var column in columns)
            {
                var builderColumn = new BuilderTableColumn
                {
                    ColumnName = column.ColumnName,
                    Comment = column.Comment,
                    ColumnType = column.ColumnType,
                    EntityType = column.EntityType,
                    EntityName = column.ColumnName,

                    IsKey = column.IsKey == 1,
                    IsRequired = column.IsNull != 1,
                    IsEdit = true,
                    IsInsert = true,
                    IsList = true,
                    MaxLength = column.MaxLength,
                    TableName = obj.TableName,
                    TableId = obj.Id,

                    CreateUserId = user.Id,
                    CreateUserName = user.Name,
                    CreateTime = DateTime.Now
                };
                UnitWork.Add(builderColumn);
            }

            UnitWork.Save();
            return obj.Id;
        }

        public void Update(AddOrUpdateBuilderTableReq obj)
        {
            var user = _auth.GetCurrentUser().User;
            UnitWork.Update<BuilderTable>(u => u.Id == obj.Id, u => new BuilderTable
            {
                TableName = obj.TableName,
                Comment = obj.Comment,
                DetailTableName = obj.DetailTableName,
                DetailComment = obj.DetailComment,
                ClassName = obj.ClassName,
                Namespace = obj.Namespace,
                ModuleCode = obj.ModuleCode,
                ModuleName = obj.ModuleName,
                Folder = obj.Folder,
                Options = obj.Options,
                TypeId = obj.TypeId,
                TypeName = obj.TypeName,
                UpdateTime = DateTime.Now,
                UpdateUserId = user.Id,
                UpdateUserName = user.Name
                //todo:补充或调整自己需要的字段
            });
        }

        /// <summary>
        /// 删除头和字段明细
        /// </summary>
        /// <param name="ids"></param>
        public void DelTableAndcolumns(string[] ids)
        {
            UnitWork.Delete<BuilderTable>(u => ids.Contains(u.Id));
            UnitWork.Delete<BuilderTableColumn>(u => ids.Contains(u.TableId));
            UnitWork.Save();
        }


        /// <summary>
        /// 生成实体Model
        /// </summary>
        /// <returns></returns>
        public void CreateEntity(CreateEntityReq req)
        {
            var sysTableInfo = Repository.FindSingle(u => u.Id == req.Id);
            var tableColumns = _builderTableColumnApp.Find(req.Id);
            if (sysTableInfo == null
                || tableColumns == null
                || tableColumns.Count == 0)
                throw new Exception("未能找到正确的模版信息");

            CheckExistsModule(sysTableInfo.ModuleName, sysTableInfo.ModuleCode);

            CreateEntityModel(tableColumns, sysTableInfo);
        }

        /// <summary>
        /// 创建实体
        /// </summary>
        /// <param name="tableColumns"></param>
        /// <param name="sysTableInfo"></param>
        private void CreateEntityModel(List<BuilderTableColumn> sysColumn, BuilderTable tableInfo)
        {
            string template = "DomainModel.html";

            string domainContent = FileHelper.ReadFile("Template\\" + template);

            StringBuilder AttributeBuilder = new StringBuilder();
            sysColumn = sysColumn.OrderByDescending(c => c.Sort).ToList();
            bool addIgnore = false;
            foreach (BuilderTableColumn column in sysColumn)
            {
                column.ColumnType = (column.ColumnType ?? "").Trim();
                AttributeBuilder.Append("/// <summary>");
                AttributeBuilder.Append("\r\n");
                AttributeBuilder.Append("       ///" + column.Comment + "");
                AttributeBuilder.Append("\r\n");
                AttributeBuilder.Append("       /// </summary>");
                AttributeBuilder.Append("\r\n");
                if (column.IsKey)
                {
                    AttributeBuilder.Append(@"       [Key]" + "");
                    AttributeBuilder.Append("\r\n");
                }

                AttributeBuilder.Append("       [Display(Name =\"" + (
                    string.IsNullOrEmpty(column.Comment) ? column.ColumnName : column.Comment
                ) + "\")]");
                AttributeBuilder.Append("\r\n");

                if (column != null && (column.ColumnType == "varchar" && column.MaxLength > 8000)
                    || (column.ColumnType == "nvarchar" && column.MaxLength > 4000))
                {
                    column.MaxLength = 0;
                }

                if (column.ColumnType == "string" && column.MaxLength > 0 && column.MaxLength < 8000)
                {
                    AttributeBuilder.Append("       [MaxLength(" + column.MaxLength + ")]");
                    AttributeBuilder.Append("\r\n");
                }


                if ((column.IsKey && (column.ColumnType == "string" || column.ColumnType == "uniqueidentifier")) ||
                    column.ColumnType.ToLower() == "guid"
                    || (IsMysql() && column.ColumnType == "string" && column.MaxLength == 36))
                {
                    column.ColumnType = "uniqueidentifier";
                }

                string MaxLength = string.Empty;
                if (column.ColumnType != "uniqueidentifier")
                {
                    if (column.IsKey && column.ColumnType.ToLower() == "string")
                    {
                        //没有指定长度的字符串字段 ，如varchar,nvarchar，text等都默认生成varchar(max),nvarchar(max)
                        if (column.MaxLength <= 0
                            || (column.ColumnType == "varchar" && column.MaxLength > 8000)
                            || (column.ColumnType == "nvarchar" && column.MaxLength > 4000))
                        {
                            MaxLength = "(max)";
                        }
                        else
                        {
                            MaxLength = "(" + column.MaxLength + ")";
                        }
                    }
                }

                AttributeBuilder.Append("       [Column(TypeName=\"" + column.ColumnType + MaxLength + "\")]");
                AttributeBuilder.Append("\r\n");


                if (column.ColumnType == "int" || column.ColumnType == "bigint" || column.ColumnType == "long")
                {
                    column.ColumnType = column.ColumnType == "int" ? "int" : "long";
                }

                if (column.ColumnType == "bool")
                {
                    column.ColumnType = "bit";
                }

                if (column.EditRow != null)
                {
                    AttributeBuilder.Append("       [Editable(true)]");
                    AttributeBuilder.Append("\r\n");
                }

                string columnType = (column.ColumnType == "Date" ? "DateTime" : column.ColumnType).Trim();
                if (column?.ColumnType?.ToLower() == "guid")
                {
                    columnType = "Guid";
                }

                if (column.ColumnType.ToLower() != "string" && !column.IsRequired)
                {
                    columnType = columnType + "?";
                }

                //如果主键是string,则默认为是Guid或者使用的是mysql数据，字段类型是字符串并且长度是36则默认为是Guid
                if ((column.IsKey
                     && (column.ColumnType == "string"
                         || column.ColumnType == "uniqueidentifier"))
                    || column.ColumnType == "guid"
                    || (IsMysql() && column.ColumnType == "string" && column.MaxLength == 36))
                {
                    columnType = "Guid" + (column.IsRequired ? "" : "?");
                }

                AttributeBuilder.Append("       public " + columnType + " " + column.ColumnName + " { get; set; }");
                AttributeBuilder.Append("\r\n\r\n       ");
            }

            if (!string.IsNullOrEmpty(tableInfo.DetailTableName))
            {
                AttributeBuilder.Append("[Display(Name =\"" + tableInfo.DetailTableName + "\")]");
                AttributeBuilder.Append("\r\n       ");
                AttributeBuilder.Append("[ForeignKey(\"" + sysColumn.Where(x => x.IsKey).FirstOrDefault().ColumnName +
                                        "\")]");
                AttributeBuilder.Append("\r\n       ");
                AttributeBuilder.Append("public List<" + tableInfo.DetailTableName + "> " + tableInfo.DetailTableName +
                                        " { get; set; }");
                AttributeBuilder.Append("\r\n");
            }

            //获取的是本地开发代码所在目录，不是发布后的目录
            string mapPath =
                ProjectPath.GetProjectDirectoryInfo()?.FullName; //new DirectoryInfo(("~/").MapPath()).Parent.FullName;
            //  string folderPath= string.Format("\\DairyStar.Framework.Core.\\DomainModels\\{0}\\", foldername);
            if (string.IsNullOrEmpty(mapPath))
            {
                throw new Exception("未找到生成的目录!");
            }

            string[] splitArrr = tableInfo.Namespace.Split('.');
            domainContent = domainContent.Replace("{TableName}", tableInfo.TableName)
                .Replace("{AttributeList}", AttributeBuilder.ToString()).Replace("{StartName}", StratName);

            List<string> entityAttribute = new List<string>();
            entityAttribute.Add("TableCnName = \"" + tableInfo.Comment + "\"");
            if (!string.IsNullOrEmpty(tableInfo.ModuleCode))
            {
                entityAttribute.Add("TableName = \"" + tableInfo.ModuleCode + "\"");
            }

            if (!string.IsNullOrEmpty(tableInfo.DetailTableName))
            {
            }


            string modelNameSpace = StratName + ".Entity";
            string tableAttr = string.Join(",", entityAttribute);
            if (tableAttr != "")
            {
                tableAttr = "[Entity(" + tableAttr + ")]";
            }

            if (!string.IsNullOrEmpty(tableInfo.TableName) && tableInfo.TableName != tableInfo.TableName)
            {
                string tableTrueName = tableInfo.TableName;

                tableAttr = tableAttr + "\r\n[Table(\"" + tableInfo.TableName + "\")]";
            }

            domainContent = domainContent.Replace("{AttributeManager}", tableAttr)
                .Replace("{Namespace}", modelNameSpace);

            string folderName = tableInfo.Folder;
            string tableName = tableInfo.TableName;


            FileHelper.WriteFile(
                mapPath +
                string.Format(
                    "\\" + modelNameSpace + "\\DomainModels\\{0}\\", folderName
                )
                , tableName + ".cs",
                domainContent);
        }

        private bool IsMysql()
        {
            return (_appConfiguration.Value.DbType == Define.DBTYPE_MYSQL);
        }

        /// <summary>
        /// 校验模块是否已经存在
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="moduleCode"></param>
        /// <exception cref="Exception"></exception>
        private void CheckExistsModule(string moduleName, string moduleCode)
        {
            //如果是第一次创建model，此处反射获取到的是已经缓存过的文件，必须重新运行项目否则新增的文件无法做判断文件是否创建，需要重新做反射实际文件，待修改...
            var compilationLibrary = DependencyContext
                .Default
                .CompileLibraries
                .Where(x => !x.Serviceable && x.Type == "project");
            foreach (var _compilation in compilationLibrary)
            {
                foreach (var entity in AssemblyLoadContext.Default
                    .LoadFromAssemblyName(new AssemblyName(_compilation.Name))
                    .GetTypes().Where(x => x.GetTypeInfo().BaseType != null
                                           && x.BaseType == typeof(BaseEntity)))
                {
                    if (entity.Name == moduleCode && !string.IsNullOrEmpty(moduleName) && moduleName != moduleCode)
                        throw new Exception($"实际表名【{moduleCode}】已创建实体，不能创建别名【{moduleName}】实体");

                    if (entity.Name != moduleName)
                    {
                        var tableAttr = entity.GetCustomAttribute<TableAttribute>();
                        if (tableAttr != null && tableAttr.Name == moduleCode)
                        {
                            throw new Exception(
                                $"实际表名【{moduleCode}】已被【{entity.Name}】创建建实体,不能创建别名【{moduleName}】实体,请将别名更换为【{entity.Name}】");
                        }
                    }
                }
            }
        }
    }
}