As of version 3.4.0, Caster transitioned to a new permissions model, allowing for more granular access control to different features of the application. This document will detail how the new system works.

# Project Membership

Projects now have members, and a User will only see a Project on their home page if a Project Membership is created for that User in the Project. Users with appropriate Permissions can access the new Project Management area under Administration or from the Project's dropdown menu to manage access to each Project.

Users with certain Administrative Permissions can access all Projects, even if they are not Members.

# Permissions

Access to features of Caster are governed by sets of Permissions. Permissions can apply globally or on a per Project basis. Examples of global Permissions are:

- CreateProjects - Allows creation of new Projects
- ViewProjects - Allows viewing all Projects and their Users and Groups
- ManageUsers - Allows for making changes to Users.

The Administration area now can be accessed by any User with View or Manage Permission to an Administration function (e.g. ViewVLANs, ManageWorkspaces, etc), but only the areas they have Permissions for will be accessible in the sidebar menu.

There are many more Permissions available. They can be viewed by going to the new Roles section of the Administration area.

# Roles

Permissions can be applied to Users by grouping them into Roles. There are two types of Roles in Caster:

- System Roles - Each User can have a System Role applied to them that gives global Permissions across all of Caster. The three default System Roles are:

  - Administrator - Has all Permissions within the system.
  - Content Developer - Has the `CreateProjects` Permissions. Users in this Role can create and manage their own Projects, but not affect any global settings or other User's Projects.
  - Observer - Has all `View` Permissions. Users in this Role can view everything in the system, but not make any changes.

  Custom System Roles can be created by Users with the `ManageRoles` Permission that include whatever Permissions are desired for that Role. This can be done in the Roles section of the Administration area.

- Project Roles - When a User is added to a Project, their Project Membership has a Project Role that applies Permissions to that User only for that specific Project. The three available Project Roles are:

  - Member - Can view and edit all objects within the Project.
  - Manager - Can perform all Project actions, including managing User access to the Project. When creating a new Project, the creator is given the `Manager` Role in that Project.
  - Observer - Can view all objects within the Project, but not many any changes.

  Custom Project Roles cannot be created.

Roles can be set on Users in the Users section of the Administration area.

Roles can also optionally be integrated with your Identity Provider. See Identity Provider Integration below.

# Groups

Another new section of Administration is Groups. Groups can be created and users can be added to Groups, creating a Group Membership. When managing access to a Project, you can add Groups to the Project in addition to adding Users. This can make it easier to add the same Users to several Projects with the same Project Role.

For example, if you want to give a group of Users read-only access to a subset of Projects, the `Observer` System Role would be too broad, as it provides read-only access to all Projects, as well as all Administrative settings. Instead, you could create a Group named `Observers`, add the appropriate Users to the Group, and then add this Group to each desired Project, with the `Observer` Project Role for just those Projects. Now, you can simply add or remove Users from this Group and they will gain or lose read-only access to those Projects.

Similarly, you can add Groups to certain Projects with Member or Manage Project Roles.

Groups can also optionally be integrated with your Identity Provider. See Identity Provider Integration below.

# Seed Data

The SeedData section of appsettings.json has been changed to support the new model. You can now use this section to add Roles, Users, and Groups on application startup. See appsettings.json for examples.

SeedData will only add objects if they do not exist. It will not modify existing Roles, Users, or Groups so as not to undo changes made in the application on every restart. It will re-create objects if they are deleted in the application, so be sure to remove them from SeedData if they are no longer wanted.

# Identity Provider Integration

Roles and Groups can optionally be integrated with the Identity Provider that is being used to authenticate to Caster. There are new settings under `ClaimsTransformation` to configure this integration. See appsettings.json. This integration is compatible with any Identity Provider that is capable of putting Roles and/or Groups into the auth token.

## Roles

If enabled, Roles from the User's auth token will be applied as if the Role was set on the User directly in Caster. The Role must exist in Caster and the name of the Role in the token must match exactly with the name of the Role in the token.

- UseRolesFromIdp: If true, Roles from the User's auth token will be used. Defaults to true.
- RolesClaimPath: The path within the User's auth token to look for Roles. Defaults to Keycloak's default value of `realm_access.roles`.

  Example: If the defaults are set, Caster will apply the `Content Developer` Role to a User whose token contains the following:

```json
  realm_access {
    roles: [
        "Content Developer"
    ]
  }
```

If multiple Roles are present in the token, or if one Role is in the token and one Role is set directly on the User in Caster, the Permissions of all of the Roles will be combined.

## Groups

If enabled, Groups from the User's auth token will be applied as if the User was a member of the Group(s) in caster. The Group must exist in Caster and the name of the Group in the token must match exactly with the name of the Group in the token.

- UseGroupsFromIdP: If true, Groups from the User's auth token will be used. Defaults to true.
- GroupsClaimPath: The path within the User's auth token to look for Groups. Defaults to groups.

Example: If the defaults are set, Caster will consider a User a member of the Power Users and Demo Groups if their token contains the following:

```json
  groups: [
    "Power Users",
    "Demo"
  ]
```

This will be combined with any Group Memberships that User has had created directly in Caster.

## Keycloak

If you are using Keycloak as your Identity Provider, Roles should work by default if you have not changed the default `RolesClaimPath`. You may need to adjust this value if your Keycloak is configured to put Roles in a different location within the token.

Groups need some additional configuration within Keycloak. In Keycloak, Groups are not added to the token by default. You can enable this by creating a new Client Scope or editing an existing one. The easiest method is to edit the default `roles` Client Scope to add Groups to it.

- From the Keycloak Administrative console, click `Client Scopes` in the sidebar
- Select the `roles` Client Scope
- Select the `Mappers` tab
- Click `Add Mapper` -> `By configuration`
- Select `Group Membership`
- Set `Name` and `Token Claim Name` to `groups`
- Disable `Full group path` and `Add to ID token`
- Enable `Add to access token` and `Add to userinfo`

Now your auth tokens should contain a `Groups` array that will include and Groups a User has been added to within Keycloak. If you want to enable this for specific applications only, you should instead add this `Groups` mapper to the specific scopes that those clients use instead of the `roles` scope.

# Migration

When moving from a version prior to 3.4.0, the database will be migrated from the old Permissions sytem to the new one. The end result should be no change in access to any existing Users.

- Any existing Users with the old `SystemAdmin` Permission will be migrated to the new `Administrator` Role
- Any existing Users with the old `ContentDeveloper` Permissions will be migrated to the new `Content Developer` Role
- Any existing Users with either of the old Permissions will be given Project Memberships to all existing Projects in order to retain existing access. You can remove these memberships if you want certain Users to have less access than before.

Be sure to double check all of your Roles, Project Memberships, and Group Memberships once the migration is complete.
