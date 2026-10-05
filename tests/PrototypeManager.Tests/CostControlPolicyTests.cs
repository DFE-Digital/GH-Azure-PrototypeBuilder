using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using PrototypeManager.Application;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class CostControlPolicyTests
{
    private readonly CostControlPolicy _policy = new(Options.Create(new CostControlOptions()));

    [Fact]
    public async Task ValidateCreateRequestAsync_AcceptsValidRequest()
    {
        var request = CreateRequest();

        await _policy.ValidateCreateRequestAsync(request);
    }

    [Fact]
    public async Task ValidateCreateRequestAsync_RejectsMissingDeclaration()
    {
        var request = CreateRequest();
        request.DeclarationAgreed = false;

        await Assert.ThrowsAsync<ValidationException>(() => _policy.ValidateCreateRequestAsync(request));
    }

    [Fact]
    public async Task ValidateCreateRequestAsync_RejectsInvalidAppName()
    {
        var request = CreateRequest();
        request.AppName = "Invalid Name";

        await Assert.ThrowsAsync<ValidationException>(() => _policy.ValidateCreateRequestAsync(request));
    }

    private static CreatePrototypeRequest CreateRequest() => new()
    {
        AppName = "teacher-contacts",
        BusinessAreaId = Guid.NewGuid(),
        Project = "Teacher contacts",
        GitHubRepositoryUrl = "https://github.com/department-for-education/teacher-contacts-alpha",
        DeclarationAgreed = true,
        PrototypePassword = "super-secure-password"
    };
}
