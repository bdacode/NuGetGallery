﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using Moq;
using NuGetGallery.Configuration;
using NuGetGallery.Framework;
using Xunit;

namespace NuGetGallery
{
    public class UsersControllerFacts
    {
        public class TheAccountAction : TestContainer
        {
            [Fact]
            public void WillGetTheCurrentUserUsingTheRequestIdentityName()
            {
                var controller = GetController<UsersController>();
                var user = new User { Username = "theUsername" };
                controller.SetUser(user);
                GetMock<IUserService>()
                          .Setup(s => s.FindByUsername(It.IsAny<string>()))
                          .Returns(user);

                //act
                controller.Account();

                // verify
                GetMock<IUserService>()
                          .Verify(stub => stub.FindByUsername("theUsername"));
            }

            [Fact]
            public void WillGetCuratedFeedsManagedByTheCurrentUser()
            {
                var controller = GetController<UsersController>();
                var user = new User { Username = "theUsername", Key = 42 };
                controller.SetUser(user);
                GetMock<IUserService>()
                          .Setup(s => s.FindByUsername(It.IsAny<string>()))
                          .Returns(user);

                // act
                controller.Account();

                // verify
                GetMock<ICuratedFeedService>()
                    .Verify(query => query.GetFeedsForManager(42));
            }

            [Fact]
            public void WillReturnTheAccountViewModelWithTheUserApiKey()
            {
                var controller = GetController<UsersController>();
                var stubApiKey = Guid.NewGuid();
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(s => s.FindByUsername(It.IsAny<string>()))
                          .Returns(new User { Key = 42, ApiKey = stubApiKey });

                // act
                var model = ((ViewResult)controller.Account()).Model as AccountViewModel;

                // verify
                Assert.Equal(stubApiKey.ToString(), model.ApiKey);
            }

            [Fact]
            public void WillReturnTheAccountViewModelWithTheCuratedFeeds()
            {
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(s => s.FindByUsername(It.IsAny<string>()))
                          .Returns(new User { Key = 42 });
                controller.MockCuratedFeedService
                          .Setup(stub => stub.GetFeedsForManager(It.IsAny<int>()))
                          .Returns(new[] { new CuratedFeed { Name = "theCuratedFeed" } });

                // act
                var model = ((ViewResult)controller.Account()).Model as AccountViewModel;

                // verify
                Assert.Equal("theCuratedFeed", model.CuratedFeeds.First());
            }

            [Fact]
            public void WillUseApiKeyInColumnIfNoCredentialPresent()
            {
                var apiKey = Guid.NewGuid();
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(s => s.FindByUsername(It.IsAny<string>()))
                          .Returns(new User { Key = 42, ApiKey = apiKey });
                controller.MockCuratedFeedService
                          .Setup(stub => stub.GetFeedsForManager(It.IsAny<int>()))
                          .Returns(new[] { new CuratedFeed { Name = "theCuratedFeed" } });

                // act
                var result = controller.Account();
                
                // verify
                var model = ResultAssert.IsView<AccountViewModel>(result);
                Assert.Equal(apiKey.ToString().ToLowerInvariant(), model.ApiKey);
            }

            [Fact]
            public void WillUseApiKeyInCredentialIfPresent()
            {
                var apiKey = Guid.NewGuid();
                var controller = new TestableUsersController();
                controller.MockUserService
                    .Setup(s => s.FindByUsername(It.IsAny<string>()))
                    .Returns(new User
                    {
                        Key = 42,
                        ApiKey = Guid.NewGuid(),
                        Credentials = new List<Credential>() {
                            CredentialBuilder.CreateV1ApiKey(apiKey)
                        }
                    });
                controller.MockCuratedFeedService
                    .Setup(stub => stub.GetFeedsForManager(It.IsAny<int>()))
                    .Returns(new[] { new CuratedFeed { Name = "theCuratedFeed" } });

                // act
                var result = controller.Account();
                
                // verify
                var model = ResultAssert.IsView<AccountViewModel>(result);
                Assert.Equal(apiKey.ToString().ToLowerInvariant(), model.ApiKey);
            }
        }

        public class TheConfirmMethod :  TestContainer
        {
            [Fact]
            public void Returns403ForbiddenWhenAuthenticatedAsWrongUser()
            {
                var controller = GetController<UsersController>();
                controller.SetUser(Fakes.User);

                var result = controller.Confirm("wrongUsername", "someToken");

                ResultAssert.IsStatusCode(result, 403, "You are not logged in as the correct user to perform this action.");
            }

            [Fact]
            public void Returns404WhenTokenIsEmpty()
            {
                var controller = GetController<UsersController>();
                controller.SetUser(Fakes.User);
                
                var result = controller.Confirm(Fakes.User.Username, "");
                
                ResultAssert.IsStatusCode(result, 404);
            }

            [Fact]
            public void ReturnsConfirmedWhenTokenMatchesUser()
            {
                var user = new User
                    {
                        UnconfirmedEmailAddress = "email@example.com",
                        EmailConfirmationToken = "the-token"
                    };
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(u => u.FindByUsername("username"))
                          .Returns(user);
                controller.MockUserService
                          .Setup(u => u.ConfirmEmailAddress(user, "the-token"))
                          .Returns(true);

                GetMock<IUserService>()
                    .Setup(u => u.FindByUsername("username"))
                    .Returns(user);
                GetMock<IUserService>()
                    .Setup(u => u.ConfirmEmailAddress(user, "the-token"))
                    .Returns(true);

                var model = (controller.Confirm("username", "the-token") as ViewResult).Model as ConfirmationViewModel;

                Assert.True(model.SuccessfulConfirmation);
            }

            [Fact]
            public void SendsAccountChangedNoticeWhenConfirmingChangedEmail()
            {
                var user = new User
                {
                    Username = "username",
                    EmailAddress = "old@example.com",
                    UnconfirmedEmailAddress = "new@example.com",
                    EmailConfirmationToken = "the-token"
                };
                var controller = GetController<UsersController>();
                controller.SetUser(user);

                GetMock<IUserService>()
                          .Setup(u => u.FindByUsername("username"))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.ConfirmEmailAddress(user, "the-token"))
                          .Returns(true);

                var model = (controller.Confirm("username", "the-token") as ViewResult).Model as EmailConfirmationModel;

                Assert.True(model.SuccessfulConfirmation);
                Assert.False(model.ConfirmingNewAccount);
                GetMock<IMessageService>()
                          .Verify(m => m.SendEmailChangeNoticeToPreviousEmailAddress(user, "old@example.com"));
            }

            [Fact]
            public void DoesntSendAccountChangedEmailsWhenNoOldConfirmedAddress()
            {
                var user = new User
                {
                    Username = "username",
                    EmailAddress = null,
                    UnconfirmedEmailAddress = "new@example.com",
                    EmailConfirmationToken = "the-token"
                };
                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(u => u.FindByUsername("username"))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.ConfirmEmailAddress(user, "the-token"))
                          .Returns(true);
                controller.SetUser(user);

                // act:
                var model = (controller.Confirm("username", "the-token") as ViewResult).Model as ConfirmationViewModel;

                // verify:
                Assert.True(model.SuccessfulConfirmation);
                Assert.True(model.ConfirmingNewAccount);
                GetMock<IMessageService>()
                          .Verify(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()), Times.Never());
                GetMock<IMessageService>()
                          .Verify(m => m.SendEmailChangeNoticeToPreviousEmailAddress(It.IsAny<User>(), It.IsAny<string>()), Times.Never());
            }

            [Fact]
            public void DoesntSendAccountChangedEmailsIfConfirmationTokenDoesntMatch()
            {
                var user = new User
                {
                    Username = "username",
                    EmailAddress = "old@example.com",
                    UnconfirmedEmailAddress = "new@example.com",
                    EmailConfirmationToken = "the-token"
                };
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(u => u.FindByUsername("username"))
                          .Returns(user);
                controller.MockUserService
                          .Setup(u => u.ConfirmEmailAddress(user, "faketoken"))
                          .Returns(false);

                // act:
                var model = (controller.Confirm("username", "faketoken") as ViewResult).Model as ConfirmationViewModel;

                // verify:
                Assert.False(model.SuccessfulConfirmation);
                Assert.False(model.ConfirmingNewAccount);
                GetMock<IMessageService>()
                    .Verify(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()), Times.Never());
                GetMock<IMessageService>()
                  .Verify(m => m.SendEmailChangeNoticeToPreviousEmailAddress(It.IsAny<User>(), It.IsAny<string>()), Times.Never());
            }

            [Fact]
            public void ReturnsFalseWhenTokenDoesNotMatchUser()
            {
                var user = new User
                {
                    Username = "username",
                    EmailAddress = "old@example.com",
                    UnconfirmedEmailAddress = "new@example.com",
                    EmailConfirmationToken = "the-token"
                };
                var controller = GetController<UsersController>();
                controller.SetUser(user);
                GetMock<IUserService>()
                          .Setup(u => u.FindByUsername("username"))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.ConfirmEmailAddress(user, "not-the-token"))
                          .Returns(false);

                var model = (controller.Confirm("username", "not-the-token") as ViewResult).Model as ConfirmationViewModel;

                Assert.False(model.SuccessfulConfirmation);
            }
        }

        public class TheEditMethod : TestContainer
        {
            [Fact]
            public void UpdatesEmailAllowedSetting()
            {
                var user = new User
                {
                    Username = "aUsername",
                    EmailAddress = "test@example.com",
                    EmailAllowed = true
                };

                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(u => u.FindByUsername(It.IsAny<string>()))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.UpdateProfile(user, false));
                controller.SetUser(user);
                var model = new EditProfileViewModel { EmailAddress = "test@example.com", EmailAllowed = false };

                var result = controller.Edit(model);

                var viewModel = ResultAssert.IsView<EditProfileViewModel>(result);
                Assert.Same(model, viewModel);
                GetMock<IUserService>().Verify(u => u.UpdateProfile(user, false));
            }

            [Fact]
            public void SendsEmailChangeConfirmationNoticeWhenEmailConfirmationTokenChanges()
            {
                var user = new User
                    {
                        EmailAddress = "test@example.com",
                        EmailAllowed = true
                    };

                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(u => u.FindByUsername(It.IsAny<string>()))
                          .Returns(user);
                controller.MockUserService
                          .Setup(u => u.UpdateProfile(user, "new@example.com", true))
                          .Callback(() => user.EmailConfirmationToken = "token");
                var model = new EditProfileViewModel { EmailAddress = "new@example.com", EmailAllowed = true };

                var result = controller.Edit(model) as RedirectToRouteResult;

                Assert.NotNull(result);
                Assert.Equal(
                    "Account settings saved! We sent a confirmation email to verify your new email. When you confirm the email address, it will take effect and we will forget the old one.",
                    controller.TempData["Message"]);
            }

            [Fact]
            public void DoesNotSendEmailChangeConfirmationNoticeWhenTokenDoesNotChange()
            {
                var user = new User
                    {
                        EmailAddress = "old@example.com",
                        EmailAllowed = true,
                        EmailConfirmationToken = "token"
                    };

                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(u => u.FindByUsername(It.IsAny<string>()))
                          .Returns(user);
                controller.MockMessageService
                          .Setup(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()))
                          .Throws(new InvalidOperationException());
                var model = new EditProfileViewModel { EmailAddress = "old@example.com", EmailAllowed = true };

                var result = controller.Edit(model) as RedirectToRouteResult;

                Assert.NotNull(result);
                Assert.Equal("Account settings saved!", controller.TempData["Message"]);
                controller.MockUserService
                          .Verify(u => u.UpdateProfile(user, It.IsAny<string>(), true));
            }

            [Fact]
            public void WithInvalidUsernameReturnsFileNotFound()
            {
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(u => u.FindByUsername(It.IsAny<string>()))
                          .ReturnsNull();

                var result = controller.Edit(new EditProfileViewModel()) as HttpNotFoundResult;

                Assert.NotNull(result);
            }
        }

        public class TheForgotPasswordMethod : TestContainer
        {
            [Fact]
            public void SendsEmailWithPasswordResetUrl()
            {
                const string resetUrl = "https://nuget.local/account/ResetPassword/somebody/confirmation";
                var user = new User
                    {
                        EmailAddress = "some@example.com",
                        Username = "somebody",
                        PasswordResetToken = "confirmation",
                        PasswordResetTokenExpirationDate = DateTime.UtcNow.AddDays(1)
                    };
                var controller = GetController<UsersController>();
                GetMock<IMessageService>()
                          .Setup(s => s.SendPasswordResetInstructions(user, resetUrl));
                GetMock<IUserService>()
                          .Setup(s => s.FindByEmailAddress(It.IsAny<string>()))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(s => s.GeneratePasswordResetToken("user", 1440))
                          .Returns(user);
                var model = new ForgotPasswordViewModel { Email = "user" };

                controller.ForgotPassword(model);

                GetMock<IMessageService>()
                    .Verify(s => s.SendPasswordResetInstructions(user, resetUrl));
            }

            [Fact]
            public void RedirectsAfterGeneratingToken()
            {
                var user = new User { EmailAddress = "some@example.com", Username = "somebody" };
                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(s => s.GeneratePasswordResetToken("user", 1440))
                          .Returns(user)
                          .Verifiable();
                var model = new ForgotPasswordViewModel { Email = "user" };

                var result = controller.ForgotPassword(model) as RedirectToRouteResult;

                Assert.NotNull(result);
                GetMock<IUserService>()
                          .Verify(s => s.GeneratePasswordResetToken("user", 1440));
            }

            [Fact]
            public void ReturnsSameViewIfTokenGenerationFails()
            {
                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(s => s.GeneratePasswordResetToken("user", 1440))
                          .Returns((User)null);

                var model = new ForgotPasswordViewModel { Email = "user" };

                var result = controller.ForgotPassword(model) as ViewResult;

                Assert.NotNull(result);
                Assert.IsNotType(typeof(RedirectResult), result);
            }
        }

        public class TheGenerateApiKeyMethod : TestContainer
        {
            [Fact]
            public void RedirectsToAccountPage()
            {
                var user = new User();
                    .Setup(i => i.Name)
                    .Returns("the-username");
                controller.MockUserService
                    .Setup(u => u.FindByUsername("the-username"))
                    .Returns(user);

                var result = controller.GenerateApiKey();

                ResultAssert.IsRedirectToRoute(result, new { action = "Account", controller = "Users" });
            }

            [Fact]
            public void PutsNewCredentialInOldField()
            {
                var controller = new TestableUsersController();
                var user = new User() { ApiKey = Guid.NewGuid() };
                controller.MockCurrentIdentity
                    .Setup(i => i.Name)
                    .Returns("the-username");
                controller.MockUserService
                    .Setup(u => u.FindByUsername("the-username"))
                    .Returns(user);
                Credential created = null;
                controller.MockUserService
                    .Setup(u => u.ReplaceCredential(user, It.IsAny<Credential>()))
                    .Callback<User, Credential>((_, c) => created = c);

                GetMock<IUserService>()
                    .Setup(u => u.FindByUsername(It.IsAny<string>()))
                    .Returns(user);

                controller.GenerateApiKey();

                Assert.Equal(created.Value, user.ApiKey.ToString().ToLowerInvariant());
            }

            [Fact]
            public void ReplacesTheApiKeyCredential()
            {
                var controller = new TestableUsersController();
                var user = new User();
                controller.MockCurrentIdentity
                    .Setup(i => i.Name)
                    .Returns("the-username");
                controller.MockUserService
                    .Setup(u => u.FindByUsername("the-username"))
                    .Returns(user);

                controller.GenerateApiKey();

                controller.MockUserService
                    .Verify(u => u.ReplaceCredential(
                        user,
                        It.Is<Credential>(c => c.Type == Constants.CredentialTypes.ApiKeyV1)));
            }
        }

        public class TheChangeEmailAction : TestContainer
        {
            [Fact]
            public void SendsEmailChangeConfirmationNoticeWhenChangingAConfirmedEmailAddress()
            {
                var user = new User
                {
                    Username = "theUsername",
                    EmailAddress = "test@example.com",
                    EmailAllowed = true
                };

                var controller = GetController<UsersController>();
                controller.SetUser(user);

                GetMock<IUserService>()
                    .Setup(u => u.FindByUsernameAndPassword(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(user);
                GetMock<IUserService>()
                    .Setup(u => u.ChangeEmailAddress(user, "new@example.com"))
                    .Callback(() => user.UpdateEmailAddress("new@example.com", () => "token"));
                var model = new ChangeEmailRequestModel { NewEmail = "new@example.com" };

                var result = controller.ChangeEmail(model);

                GetMock<IMessageService>()
                    .Verify(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()));
            }

            [Fact]
            public void DoesNotSendEmailChangeConfirmationNoticeWhenAddressDoesntChange()
            {
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                          .Returns(new User { Username = "theUsername", EmailAddress = "to@example.com" });

                var controller = GetController<UsersController>();
                controller.SetUser(user);
                GetMock<IUserService>()
                          .Setup(u => u.FindByUsernameAndPassword(It.IsAny<string>(), It.IsAny<string>()))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.ChangeEmailAddress(It.IsAny<User>(), It.IsAny<string>()))
                          .Callback(() => user.UpdateEmailAddress("old@example.com", () => "new-token"));

                var model = new ChangeEmailRequestModel { NewEmail = "old@example.com" };

                controller.ChangeEmail(model);

                GetMock<IUserService>()
                    .Verify(u => u.ChangeEmailAddress(user, "old@example.com"), Times.Never());
                GetMock<IMessageService>()
                    .Verify(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()), Times.Never());
            }

            [Fact]
            public void DoesNotSendEmailChangeConfirmationNoticeWhenUserWasNotConfirmed()
            {
                var controller = new TestableUsersController();
                controller.MockUserService
                          .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                          .Throws(new EntityException("aMessage"));

                var controller = GetController<UsersController>();
                controller.SetUser(user);
                GetMock<IUserService>()
                          .Setup(u => u.FindByUsernameAndPassword(It.IsAny<string>(), It.IsAny<string>()))
                          .Returns(user);
                GetMock<IUserService>()
                          .Setup(u => u.ChangeEmailAddress(It.IsAny<User>(), It.IsAny<string>()))
                          .Callback(() => user.UpdateEmailAddress("new@example.com", () => "new-token"));
                var model = new ChangeEmailRequestModel{ NewEmail = "new@example.com" };

                controller.ChangeEmail(model);

                Assert.Equal("Your new email address was saved!", controller.TempData["Message"]);
                GetMock<IUserService>()
                    .Verify(u => u.ChangeEmailAddress(user, "new@example.com"));
                GetMock<IMessageService>()
                    .Verify(m => m.SendEmailChangeConfirmationNotice(It.IsAny<MailAddress>(), It.IsAny<string>()), Times.Never());
            }
        }

        public class TheConfirmAction : TestContainer
        {
            [Fact]
            public void WillSendNewUserEmailWhenPosted()
            {
                var user = new User
                {
                    Username = "theUsername",
                    UnconfirmedEmailAddress = "to@example.com",
                    EmailConfirmationToken = "confirmation"
                };

                string sentConfirmationUrl = null;
                MailAddress sentToAddress = null;

                var controller = GetController<UsersController>();
                controller.SetUser(user);

                GetMock<IUserService>()
                    .Setup(x => x.FindByUsername(It.IsAny<string>()))
                    .Returns(user);
                GetMock<IUserService>()
                    .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(user);
                GetMock<IMessageService>()
                    .Setup(m => m.SendNewAccountEmail(It.IsAny<MailAddress>(), It.IsAny<string>()))
                    .Callback<MailAddress, string>((to, url) =>
                    {
                        sentToAddress = to;
                        sentConfirmationUrl = url;
                    });

                controller.ConfirmationRequiredPost();

                // We use a catch-all route for unit tests so we can see the parameters 
                // are passed correctly.
                Assert.Equal("https://nuget.local/account/Confirm/theUsername/confirmation", sentConfirmationUrl);
                Assert.Equal("to@example.com", sentToAddress.Address);
            }
        }

        public class TheChangePasswordMethod
        {
            [Fact]
            public void ReturnsViewIfModelStateInvalid()
            {
                // Arrange
                var controller = new TestableUsersController();
                controller.ModelState.AddModelError("test", "test");
                var inputModel = new PasswordChangeViewModel();

                // Act
                var result = controller.ChangePassword(inputModel);

                // Assert
                var outputModel = ResultAssert.IsView<PasswordChangeViewModel>(result);
                Assert.Same(inputModel, outputModel);
            }

            [Fact]
            public void AddsModelErrorIfUserServiceFails()
            {
                // Arrange
                var controller = new TestableUsersController();
                controller.MockCurrentIdentity
                    .Setup(i => i.Name)
                    .Returns("user");
                controller.MockUserService
                    .Setup(u => u.ChangePassword("user", "old", "new"))
                    .Returns(false);
                var inputModel = new PasswordChangeViewModel()
                {
                    OldPassword = "old",
                    NewPassword = "new",
                    ConfirmPassword = "new"
                };

                // Act
                var result = controller.ChangePassword(inputModel);

                // Assert
                var outputModel = ResultAssert.IsView<PasswordChangeViewModel>(result);
                Assert.Same(inputModel, outputModel);

                var errorMessages = controller
                    .ModelState["OldPassword"]
                    .Errors
                    .Select(e => e.ErrorMessage)
                    .ToArray();
                Assert.Equal(errorMessages, new[] { Strings.CurrentPasswordIncorrect });
            }

            [Fact]
            public void RedirectsToPasswordChangedIfUserServiceSucceeds()
            {
                // Arrange
                var controller = new TestableUsersController();
                controller.MockCurrentIdentity
                    .Setup(i => i.Name)
                    .Returns("user");
                controller.MockUserService
                    .Setup(u => u.ChangePassword("user", "old", "new"))
                    .Returns(true);
                var inputModel = new PasswordChangeViewModel()
                {
                    OldPassword = "old",
                    NewPassword = "new",
                    ConfirmPassword = "new"
                };

                // Act
                var result = controller.ChangePassword(inputModel);

                // Assert
                ResultAssert.IsRedirectToRoute(result, new
                {
                    controller = "Users",
                    action = "PasswordChanged"
                });
            }
        }

        public class TheResetPasswordMethod
        {
            [Fact]
            public void ShowsErrorIfTokenExpired()
            {
                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(u => u.ResetPasswordWithToken("user", "token", "newpwd"))
                          .Returns(false);
                var model = new PasswordResetViewModel
                    {
                        ConfirmPassword = "pwd",
                        NewPassword = "newpwd"
                    };

                controller.ResetPassword("user", "token", model);

                Assert.Equal("The Password Reset Token is not valid or expired.", controller.ModelState[""].Errors[0].ErrorMessage);
                GetMock<IUserService>()
                          .Verify(u => u.ResetPasswordWithToken("user", "token", "newpwd"));
            }

            [Fact]
            public void ResetsPasswordForValidToken()
            {
                var controller = GetController<UsersController>();
                GetMock<IUserService>()
                          .Setup(u => u.ResetPasswordWithToken("user", "token", "newpwd"))
                          .Returns(true);
                var model = new PasswordResetViewModel
                    {
                        ConfirmPassword = "pwd",
                        NewPassword = "newpwd"
                    };

                var result = controller.ResetPassword("user", "token", model) as RedirectToRouteResult;

                Assert.NotNull(result);
                GetMock<IUserService>()
                          .Verify(u => u.ResetPasswordWithToken("user", "token", "newpwd"));
            }
        }

        public class TheThanksMethod : TestContainer
        {
            [Fact]
            public void ShowsDefaultThanksView()
            {
                var controller = GetController<UsersController>();
                GetMock<IAppConfiguration>()
                          .Setup(x => x.ConfirmEmailAddresses)
                          .Returns(true);

                var result = controller.Thanks() as ViewResult;

                Assert.Empty(result.ViewName);
                Assert.Null(result.Model);
            }

            [Fact]
            public void ShowsConfirmViewWithModelWhenConfirmingEmailAddressIsNotRequired()
            {
                var controller = new TestableUsersController();
                controller.MockConfig
                          .Setup(x => x.ConfirmEmailAddresses)
                          .Returns(false);

                var result = controller.Thanks() as ViewResult;

                Assert.Equal("Confirm", result.ViewName);
                var model = result.Model as EmailConfirmationModel;
                Assert.True(model.ConfirmingNewAccount);
                Assert.True(model.SuccessfulConfirmation);
            }
        }

        public class TestableUsersController : UsersController
        {
            public Mock<ICuratedFeedService> MockCuratedFeedService { get; protected set; }
            public Mock<IPrincipal> MockCurrentUser { get; protected set; }
            public Mock<IIdentity> MockCurrentIdentity { get; protected set; }
            public Mock<IMessageService> MockMessageService { get; protected set; }
            public Mock<IPackageService> MockPackageService { get; protected set; }
            public Mock<IAppConfiguration> MockConfig { get; protected set; }
            public Mock<IUserService> MockUserService { get; protected set; }

            public TestableUsersController()
            {
                CuratedFeedService = (MockCuratedFeedService = new Mock<ICuratedFeedService>()).Object;
                CurrentUser = (MockCurrentUser = new Mock<IPrincipal>()).Object;
                MessageService = (MockMessageService = new Mock<IMessageService>()).Object;
                PackageService = (MockPackageService = new Mock<IPackageService>()).Object;
                Config = (MockConfig = new Mock<IAppConfiguration>()).Object;
                UserService = (MockUserService = new Mock<IUserService>()).Object;

                MockCurrentIdentity = new Mock<IIdentity>();
                MockCurrentUser.Setup(u => u.Identity).Returns(MockCurrentIdentity.Object);

                var mockContext = new Mock<HttpContextBase>();
                TestUtility.SetupHttpContextMockForUrlGeneration(mockContext, this);
            }
        }
    }
}
