using Domain.Sharedkernel.Util;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Infrastructure.Persistence;


public static class DefaultSeedData
{
    public enum Entities
    {
        X, ApiResource, Group, GroupPermission, Menu, MenuCategory, Module, Permission, PermissionApiResource, PermissionUiResource, Resource, Role, RolePermission, Tenant, TenantModule, TenantPermissionRestriction, UiResource, User, UserGroup, UserPermission, UserRole
    }

    public static Tenant MasterTenant = new(Guid(Entities.Tenant, 1), null, "Master Tenant", "shinera", "shinera.ir", true);
    public static IReadOnlyList<Tenant> Tenants = new List<Tenant>
    {
         new(Guid(Entities.Tenant,2),Guid(Entities.Tenant,1),"LineCafe","linecafe", "line.ir", true),
         new(Guid(Entities.Tenant,3),Guid(Entities.Tenant,1),"Restafe","restafe", "restafe.ir", true),
         new(Guid(Entities.Tenant,4),Guid(Entities.Tenant,1),"BeauNail", "beaunail", "beaunail.ir", true),

    };
    public static IReadOnlyList<Module> Modules = new List<Module>
    {
        new(Guid(Entities.Module,1),"system","سیستم","مدیریت سیستم",0),
        new(Guid(Entities.Module,2),"tenant","کسب و کار","مدیریت کسب و کار",1),
    };
    public static IReadOnlyList<Resource> Resources = new List<Resource>
    {
        new(Guid(Entities.Resource,1),Guid(Entities.Module,1),"tenants","مستاجرها","مدیریت مستاجرها",0),
        new(Guid(Entities.Resource,2),Guid(Entities.Module,1),"modules","ماژول ها","مدیریت ماژول ها",1),
        new(Guid(Entities.Resource,3),Guid(Entities.Module,1),"resources","منابع ","مدیریت  منابع",1),
        new(Guid(Entities.Resource,4),Guid(Entities.Module,2),"users","کاربران ","مدیریت  کاربران",1),
        new(Guid(Entities.Resource,5),Guid(Entities.Module,1),"users","منو ","مدیریت  منو",1),

    };


    public static IReadOnlyList<ApiResource> ApiResources = new List<ApiResource>
    {
        new(Guid(Entities.ApiResource,1),Guid(Entities.Resource,2),"SYSTEM.MODULES.POST","ModulesModuleCreate",null,Domain.Entities.HttpMethod.POST,"/api/system/modules/"),
        new(Guid(Entities.ApiResource,2),Guid(Entities.Resource,2),"SYSTEM.MODULES.ID.DELETE","ModulesModuleDelete",null,Domain.Entities.HttpMethod.DELETE,"/api/system/modules/{id}"),
        new(Guid(Entities.ApiResource,3),Guid(Entities.Resource,2),"SYSTEM.MODULES.ID.GET","ModulesModuleGetById",null,Domain.Entities.HttpMethod.GET,"/api/system/modules/{id}"),
        new(Guid(Entities.ApiResource,4),Guid(Entities.Resource,2),"SYSTEM.MODULES.ID.PUT","ModulesModuleUpdate",null,Domain.Entities.HttpMethod.PUT,"/api/system/modules/{id}"),
        new(Guid(Entities.ApiResource,5),Guid(Entities.Resource,2),"SYSTEM.MODULES.LOOKUP.GET","ModulesModuleLookup",null,Domain.Entities.HttpMethod.GET,"/api/system/modules/lookup"),
        new(Guid(Entities.ApiResource,6),Guid(Entities.Resource,2),"SYSTEM.MODULES.PAGEDLIST.POST","ModulesModulePagedList",null,Domain.Entities.HttpMethod.POST,"/api/system/modules/pagedlist"),
        new(Guid(Entities.ApiResource,7),Guid(Entities.Resource,2),"SYSTEM.MODULES.ID.PATCH","ModulesModuleChangeState",null,Domain.Entities.HttpMethod.PATCH,"/api/system/modules/{id}"),

        new(Guid(Entities.ApiResource,8),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.POST","ResourcesResourceCreate",null,Domain.Entities.HttpMethod.POST,"/api/system/resources/"),
        new(Guid(Entities.ApiResource,9),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.ID.DELETE","ResourcesResourceDelete",null,Domain.Entities.HttpMethod.DELETE,"/api/system/resources/{id}"),
        new(Guid(Entities.ApiResource,10),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.ID.GET","ResourcesResourceGetById",null,Domain.Entities.HttpMethod.GET,"/api/system/resources/{id}"),
        new(Guid(Entities.ApiResource,11),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.ID.PUT","ResourcesResourceUpdate",null,Domain.Entities.HttpMethod.PUT,"/api/system/resources/{id}"),
        new(Guid(Entities.ApiResource,12),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.LOOKUP.GET","ResourcesResourceLookup",null,Domain.Entities.HttpMethod.GET,"/api/system/resources/lookup"),
        new(Guid(Entities.ApiResource,13),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.PAGEDLIST.POST","ResourcesResourcePagedList",null,Domain.Entities.HttpMethod.POST,"/api/system/resources/pagedlist"),
        new(Guid(Entities.ApiResource,14),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.ID.PATCH","ResourcesResourceChangeState",null,Domain.Entities.HttpMethod.PATCH,"/api/system/resources/{id}"),

        new(Guid(Entities.ApiResource,15),Guid(Entities.Resource,1),"SYSTEM.TENANTS.POST","TenantsTenantCreate",null,Domain.Entities.HttpMethod.POST,"/api/system/tenants/"),
        new(Guid(Entities.ApiResource,16),Guid(Entities.Resource,1),"SYSTEM.TENANTS.ID.DELETE","TenantsTenantDelete",null,Domain.Entities.HttpMethod.DELETE,"/api/system/tenants/{id}"),
        new(Guid(Entities.ApiResource,17),Guid(Entities.Resource,1),"SYSTEM.TENANTS.ID.GET","TenantsTenantGetById",null,Domain.Entities.HttpMethod.GET,"/api/system/tenants/{id}"),
        new(Guid(Entities.ApiResource,18),Guid(Entities.Resource,1),"SYSTEM.TENANTS.ID.PUT","TenantsTenantUpdate",null,Domain.Entities.HttpMethod.PUT,"/api/system/tenants/{id}"),
        new(Guid(Entities.ApiResource,19),Guid(Entities.Resource,1),"SYSTEM.TENANTS.LOOKUP.GET","TenantsTenantLookup",null,Domain.Entities.HttpMethod.GET,"/api/system/tenants/lookup"),
        new(Guid(Entities.ApiResource,20),Guid(Entities.Resource,1),"SYSTEM.TENANTS.PAGEDLIST.POST","TenantsTenantPagedList",null,Domain.Entities.HttpMethod.POST,"/api/system/tenants/pagedlist"),
        new(Guid(Entities.ApiResource,21),Guid(Entities.Resource,1),"SYSTEM.TENANTS.ID.PATCH","TenantsTenantChangeState",null,Domain.Entities.HttpMethod.PATCH,"/api/system/tenants/{id}"),

        new(Guid(Entities.ApiResource,22),Guid(Entities.Resource,2),"SYSTEM.USERS.POST","UsersUserCreate",null,Domain.Entities.HttpMethod.POST,"/api/system/users/"),
        new(Guid(Entities.ApiResource,23),Guid(Entities.Resource,2),"SYSTEM.USERS.ID.DELETE","UsersUserDelete",null,Domain.Entities.HttpMethod.DELETE,"/api/system/users/{id}"),
        new(Guid(Entities.ApiResource,24),Guid(Entities.Resource,2),"SYSTEM.USERS.ID.GET","UsersUserGetById",null,Domain.Entities.HttpMethod.GET,"/api/system/users/{id}"),
        new(Guid(Entities.ApiResource,25),Guid(Entities.Resource,2),"SYSTEM.USERS.ID.PUT","UsersUserUpdate",null,Domain.Entities.HttpMethod.PUT,"/api/system/users/{id}"),
        new(Guid(Entities.ApiResource,26),Guid(Entities.Resource,2),"SYSTEM.USERS.LOOKUP.GET","UsersUserLookup",null,Domain.Entities.HttpMethod.GET,"/api/system/users/lookup"),
        new(Guid(Entities.ApiResource,27),Guid(Entities.Resource,2),"SYSTEM.USERS.PAGEDLIST.POST","UsersUserPagedList",null,Domain.Entities.HttpMethod.POST,"/api/system/users/pagedlist"),
        new(Guid(Entities.ApiResource,28),Guid(Entities.Resource,2),"SYSTEM.USERS.ID.PATCH","UsersUserChangeState",null,Domain.Entities.HttpMethod.PATCH,"/api/system/users/{id}"),
    };


    public static IReadOnlyList<UiResource> UiResources = new List<UiResource>
    {
        new(Guid(Entities.UiResource,1),Guid(Entities.Resource,2),"SYSTEM.MODULES.CREATE.BUTTON","دکمه ایجاد ماژول",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,2),Guid(Entities.Resource,2),"SYSTEM.MODULES.UPDATE.BUTTON","دکمه ویرایش ماژول",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,3),Guid(Entities.Resource,2),"SYSTEM.MODULES.DELETE.BUTTON","دکمه حذف ماژول",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,4),Guid(Entities.Resource,2),"SYSTEM.MODULES.DELETE.BUTTON","دکمه فعالسازی ماژول",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,5),Guid(Entities.Resource,2),"SYSTEM.MODULES.LIST.PAGE","لیست ماژول ها",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,6),Guid(Entities.Resource,2),"SYSTEM.MODULES.CREATE.PAGE","ایجاد ماژول",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,7),Guid(Entities.Resource,2),"SYSTEM.MODULES.UPDATE.PAGE","ویرایش ماژول",null,UiResourceType.PAGE),

        new(Guid(Entities.UiResource,8),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.CREATE.BUTTON","دکمه ایجاد منبع",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,9),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.UPDATE.BUTTON","دکمه ویرایش منبع",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,10),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.UPDATE.BUTTON","دکمه فعالسازی منبع",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,11),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.DELETE.BUTTON","دکمه حذف منبع",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,12),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.LIST.PAGE","لیست  منابع",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,13),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.CREATE.PAGE","ایجاد منبع",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,14),Guid(Entities.Resource,3),"SYSTEM.RESOURCES.UPDATE.PAGE","ویرایش منبع",null,UiResourceType.PAGE),

        new(Guid(Entities.UiResource,15),Guid(Entities.Resource,1),"SYSTEM.TENANTS.CREATE.BUTTON","دکمه ایجاد مستاجر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,16),Guid(Entities.Resource,1),"SYSTEM.TENANTS.UPDATE.BUTTON","دکمه ویرایش مستاجر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,17),Guid(Entities.Resource,1),"SYSTEM.TENANTS.UPDATE.BUTTON","دکمه فعالسازی مستاجر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,18),Guid(Entities.Resource,1),"SYSTEM.TENANTS.DELETE.BUTTON","دکمه حذف مستاجر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,19),Guid(Entities.Resource,1),"SYSTEM.TENANTS.LIST.PAGE","لیست مستاجر ها",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,20),Guid(Entities.Resource,1),"SYSTEM.TENANTS.CREATE.PAGE","ایجاد مستاجر",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,21),Guid(Entities.Resource,1),"SYSTEM.TENANTS.UPDATE.PAGE","ویرایش مستاجر",null,UiResourceType.PAGE),

        new(Guid(Entities.UiResource,22),Guid(Entities.Resource,1),"SYSTEM.USERS.CREATE.BUTTON","دکمه ایجاد کاربر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,23),Guid(Entities.Resource,1),"SYSTEM.USERS.UPDATE.BUTTON","دکمه ویرایش کاربر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,24),Guid(Entities.Resource,1),"SYSTEM.USERS.UPDATE.BUTTON","دکمه فعالسازی کاربر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,25),Guid(Entities.Resource,1),"SYSTEM.USERS.DELETE.BUTTON","دکمه حذف کاربر",null,UiResourceType.ACTION),
        new(Guid(Entities.UiResource,26),Guid(Entities.Resource,1),"SYSTEM.USERS.LIST.PAGE","لیست کاربران",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,27),Guid(Entities.Resource,1),"SYSTEM.USERS.CREATE.PAGE","ایجاد کاربر",null,UiResourceType.PAGE),
        new(Guid(Entities.UiResource,28),Guid(Entities.Resource,1),"SYSTEM.USERS.UPDATE.PAGE","ویرایش کاربر",null,UiResourceType.PAGE),

    };


   




    private static Guid Guid(Entities entities, ulong id) => StructuredGuidV7.CreateDeterministic((ushort)entities, id);


    public static IReadOnlyList<TenantModule> TenantModules = new List<TenantModule>
    {
        new(Guid(Entities.TenantModule,1),Guid(Entities.Module,1),Guid(Entities.Tenant,1))
    };
    public static IReadOnlyList<Permission> Permissions = new List<Permission>
    {
        new(Guid(Entities.Permission,1),"modules.create",Guid(Entities.Resource,2),"ایجاد ماژول ","ایجاد ماژول ",true,[Guid(Entities.ApiResource,1)],[Guid(Entities.UiResource,1),Guid(Entities.UiResource,5)]),
        new(Guid(Entities.Permission,2),"modules.update",Guid(Entities.Resource,2),"مدیریت ماژول ","ویرایش ماژول ",true,[Guid(Entities.ApiResource, 7), Guid(Entities.ApiResource,4),Guid(Entities.ApiResource,3)],[Guid(Entities.UiResource, 4), Guid(Entities.UiResource, 6),Guid(Entities.UiResource,2)]),
        new(Guid(Entities.Permission,3),"modules.delete",Guid(Entities.Resource,2),"حذف ماژول ","حذف ماژول ",true,[Guid(Entities.ApiResource,2)],[Guid(Entities.UiResource,3)]),
        new(Guid(Entities.Permission,4),"modules.list",Guid(Entities.Resource,2),"مدیریت ماژول ها","مدیریت ماژول ها",true,[Guid(Entities.ApiResource,6)],[Guid(Entities.UiResource,5)]),


        new(Guid(Entities.Permission,5),"resources.create",Guid(Entities.Resource,3),"ایجاد منابع ","ایجاد منابع "),
        new(Guid(Entities.Permission,6),"resources.update",Guid(Entities.Resource,3),"ویرایش منابع ","ویرایش منابع "),
        new(Guid(Entities.Permission,7),"resources.delete",Guid(Entities.Resource,3),"حذف منبع ","حذف منبع "),
        new(Guid(Entities.Permission,8),"resources.list",Guid(Entities.Resource,3),"مدیریت منابع ","مدیریت منابع "),


        new(Guid(Entities.Permission,9),"tenants.create",Guid(Entities.Resource,1),"ایجاد مستاچر","ایجاد مستاچر"),
        new(Guid(Entities.Permission,10),"tenants.update",Guid(Entities.Resource,1),"ویرایش مستاچرها","ویرایش مستاجرها",true,[Guid(Entities.ApiResource,20),],[Guid(Entities.UiResource,16)]),
        new(Guid(Entities.Permission,11),"tenants.delete",Guid(Entities.Resource,1),"حذف مستاچر","حذف مستاچر"),
        new(Guid(Entities.Permission,12),"tenants.list",Guid(Entities.Resource,1),"مدیریت مستاچرها","مدیریت مستاجرها",true,[Guid(Entities.ApiResource,20)],[Guid(Entities.UiResource,16)]),
       
        
        new(Guid(Entities.Permission,13),"menus.list",Guid(Entities.Resource,1),"مدیریت منو","مدیریت منو",true,[],[]),



    };
    public static IReadOnlyList<Role> Roles = new List<Role>
    {
        new(Guid(Entities.Role,1),Guid(Entities.Tenant,1),"superadmin","مدیر سیستم"),
        new(Guid(Entities.Role,2),Guid(Entities.Tenant,2),"admin","مدیر")
    };
    public static IReadOnlyList<User> Users = new List<User>
    {
        new(Guid(Entities.User,1),null,"admin","admin@master.ir","admin","admin",UserType.System),
        //new(Guid(Entities.User,2),Guid(Entities.Tenant,3),"admin","admin@restafe.ir","admin","admin"),
        //new(Guid(Entities.User,3),Guid(Entities.Tenant,2),"admin","admin@line.ir","admin","admin"),
        //new(Guid(Entities.User,4)!,Guid(Entities.Tenant,4),"admin","super@beaunail.ir","admin","admin"),
    };
    public static IReadOnlyList<UserRole> UserRoles = new List<UserRole>
    {
        new (Guid(Entities.UserRole,1),Guid(Entities.Tenant,1),Guid(Entities.User,1),Guid(Entities.Role,1)),
        new (Guid(Entities.UserRole,2),Guid(Entities.Tenant,2),Guid(Entities.User,3),Guid(Entities.Role,2)),
        new (Guid(Entities.UserRole,3),Guid(Entities.Tenant,3),Guid(Entities.User,2),Guid(Entities.Role,2)),
        new (Guid(Entities.UserRole,4),Guid(Entities.Tenant,4),Guid(Entities.User,4),Guid(Entities.Role,2)),
       
    };

    public static IReadOnlyList<RolePermission> RolePermissions = new List<RolePermission>
    {
        new(Guid(Entities.Tenant,1),Guid(Entities.Role,1),Guid(Entities.Permission,1)),
        new(Guid(Entities.Tenant,1),Guid(Entities.Role,1),Guid(Entities.Permission,2)),
        new(Guid(Entities.Tenant,1),Guid(Entities.Role,1),Guid(Entities.Permission,3)),
        new(Guid(Entities.Tenant,1),Guid(Entities.Role,1),Guid(Entities.Permission,4)),
        new(Guid(Entities.Tenant,1),Guid(Entities.Role,1),Guid(Entities.Permission,13)),

    };
    public static IReadOnlyList<MenuCategory> MenuCategories = new List<MenuCategory>
    {
        new("مدیریت سیستم",0,[
            new(title: "مدیریت مستاجرها",icon: "business",order: 0,route: "/system/admin/tenants",permission: Guid(Entities.Permission,1)),
            new(title: "مدیریت ماژول ها",icon: "settings",order: 1,route: "/system/admin/modules",permission: Guid(Entities.Permission,3)),
            new(title: "مدیریت منابع ",icon: "source",order: 2,route: "/system/admin/resources",permission: Guid(Entities.Permission,8)),
            new(title: "مدیریت منو ",icon: "menu",order: 2,route: "/system/admin/menu-management",permission: Guid(Entities.Permission,13)),
            ]),
         new("مدیریت",1,[
            new(title: "مدیریت کاربران",icon: "business",order: 0,route: "/system/users",permission: Guid(Entities.Permission,1)),
            new(title: "مدیریت نقش ها",icon: "business",order: 0,route: "/system/roles",permission: Guid(Entities.Permission,1)),
            new(title: "مدیریت گروه های کاربری",icon: "business",order: 0,route: "/system/groups",permission: Guid(Entities.Permission,1)),
            ])
    };
}

