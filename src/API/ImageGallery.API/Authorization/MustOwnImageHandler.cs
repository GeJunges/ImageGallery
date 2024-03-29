﻿using System;
using System.Linq;
using System.Threading.Tasks;
using ImageGallery.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ImageGallery.API.Authorization
{
    public class MustOwnImageHanddler : AuthorizationHandler<MustOwnImageRequirement>
    {
        private readonly IGalleryRepository repository;
        public MustOwnImageHanddler(IGalleryRepository repository)
        {
            this.repository = repository;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MustOwnImageRequirement requirement)
        {
            var filterContext = context.Resource as AuthorizationFilterContext;

            if (filterContext == null)
            {
                context.Fail();
                return Task.CompletedTask;
            }

            var imageId = filterContext.RouteData.Values["id"].ToString();

            if (!Guid.TryParse(imageId, out Guid imageIdAsGuid))
            {
                context.Fail();
                return Task.CompletedTask;
            }

            var ownerId = context.User.Claims.FirstOrDefault(c => c.Type == "sub").Value;

            if (!repository.IsImageOwner(imageIdAsGuid, ownerId))
            {
                context.Fail();
                return Task.CompletedTask;
            }

            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
