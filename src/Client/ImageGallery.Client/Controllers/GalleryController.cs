﻿using IdentityModel.Client;
using ImageGallery.Client.Services;
using ImageGallery.Client.ViewModels;
using ImageGallery.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;


namespace ImageGallery.Client.Controllers
{
    [Authorize]
    public class GalleryController : Controller
    {
        private readonly IImageGalleryHttpClient imageGalleryHttpClient;

        public GalleryController(IImageGalleryHttpClient imageGalleryHttpClient)
        {
            this.imageGalleryHttpClient = imageGalleryHttpClient;
        }

        public async Task<IActionResult> Index()
        {
            await WriteOutIdentityInformation();
            // call the API
            var httpClient = await imageGalleryHttpClient.GetClient();

            var response = await httpClient.GetAsync("api/images").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var imagesAsString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                var galleryIndexViewModel = new GalleryIndexViewModel(
                    JsonConvert.DeserializeObject<IList<Model.Image>>(imagesAsString).ToList());

                return View(galleryIndexViewModel);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                     response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return RedirectToAction("AccessDenied", "Authorization");
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        public async Task<IActionResult> EditImage(Guid id)
        {
            // call the API
            var httpClient = await imageGalleryHttpClient.GetClient();

            var response = await httpClient.GetAsync($"api/images/{id}").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var imageAsString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var deserializedImage = JsonConvert.DeserializeObject<Model.Image>(imageAsString);

                var editImageViewModel = new EditImageViewModel()
                {
                    Id = deserializedImage.Id,
                    Title = deserializedImage.Title
                };

                return View(editImageViewModel);
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditImage(EditImageViewModel editImageViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View();
            }

            // create an ImageForUpdate instance
            var imageForUpdate = new ImageForUpdate()
            { Title = editImageViewModel.Title };

            // serialize it
            var serializedImageForUpdate = JsonConvert.SerializeObject(imageForUpdate);

            // call the API
            var httpClient = await imageGalleryHttpClient.GetClient();

            var response = await httpClient.PutAsync(
                $"api/images/{editImageViewModel.Id}",
                new StringContent(serializedImageForUpdate, System.Text.Encoding.Unicode, "application/json"))
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        public async Task<IActionResult> DeleteImage(Guid id)
        {
            // call the API
            var httpClient = await imageGalleryHttpClient.GetClient();

            var response = await httpClient.DeleteAsync($"api/images/{id}").ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        [Authorize(Roles = "PayingUser")]
        public IActionResult AddImage()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "PayingUser")]
        public async Task<IActionResult> AddImage(AddImageViewModel addImageViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View();
            }

            // create an ImageForCreation instance
            var imageForCreation = new ImageForCreation()
            { Title = addImageViewModel.Title };

            // take the first (only) file in the Files list
            var imageFile = addImageViewModel.Files.First();

            if (imageFile.Length > 0)
            {
                using (var fileStream = imageFile.OpenReadStream())
                using (var ms = new MemoryStream())
                {
                    fileStream.CopyTo(ms);
                    imageForCreation.Bytes = ms.ToArray();
                }
            }

            // serialize it
            var serializedImageForCreation = JsonConvert.SerializeObject(imageForCreation);

            // call the API
            var httpClient = await imageGalleryHttpClient.GetClient();

            var response = await httpClient.PostAsync(
                $"api/images",
                new StringContent(serializedImageForCreation, System.Text.Encoding.Unicode, "application/json"))
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return RedirectToAction("Index");
            }

            throw new Exception($"A problem happened while calling the API: {response.ReasonPhrase}");
        }

        //[Authorize(Roles = "PayingUser")]
        [Authorize(Policy = "CanOrderFrame")]
        public async Task<IActionResult> OrderFrame()
        {
            var discoveryClient = new DiscoveryClient("https://localhost:5003/");
            var metaDataResponse = await discoveryClient.GetAsync();
            var userInfoClient = new UserInfoClient(metaDataResponse.UserInfoEndpoint);
            var accessToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);
            var response = await userInfoClient.GetAsync(accessToken);

            if (response.IsError)
            {
                throw new Exception("Problem accessing the UserInfo endpoint.", response.Exception);
            }
            var address = response.Claims.FirstOrDefault(c => c.Type == "address")?.Value;
            return View(new OrderFrameViewModel(address));
        }

        public async Task WriteOutIdentityInformation()
        {
            var identityToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.IdToken);

            Debug.WriteLine($"Identity token: {identityToken}");

            foreach (var claim in User.Claims)
            {
                Debug.WriteLine($"Claim Type: {claim.Type} - Claim Value: {claim.Value}");
            }
        }

        public async Task Logout()
        {
            var discoveryClient = new DiscoveryClient("https://localhost:5003/");
            var metaDataResponse = await discoveryClient.GetAsync();
            var revocationClient = new TokenRevocationClient(metaDataResponse.RevocationEndpoint, "imagegalleryclient", "secret");
            await RevokeAccessToken(revocationClient);
            await RevokeRefreshToken(revocationClient);

            await HttpContext.SignOutAsync("Cookies");
            await HttpContext.SignOutAsync("oidc");
        }

        private async Task RevokeRefreshToken(TokenRevocationClient revocationClient)
        {
            var refreshToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.RefreshToken);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var revokeRefreshTokenResponse = await revocationClient.RevokeAccessTokenAsync(refreshToken);

                if (revokeRefreshTokenResponse.IsError)
                {
                    throw new Exception("Problem encountered while revoking the refresh token.", revokeRefreshTokenResponse.Exception);
                }
            }
        }

        private async Task RevokeAccessToken(TokenRevocationClient revocationClient)
        {
            var accessToken = await HttpContext.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var revokeAccessTokenResponse = await revocationClient.RevokeAccessTokenAsync(accessToken);

                if (revokeAccessTokenResponse.IsError)
                {
                    throw new Exception("Problem encountered while revoking the access token.", revokeAccessTokenResponse.Exception);
                }
            }
        }
    }
}
