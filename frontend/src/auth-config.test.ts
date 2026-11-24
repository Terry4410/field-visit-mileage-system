import{
  describe,
  expect,
  it
}from"vitest";
import{
  normalizeFrontendAuthMode,
  validateEntraFrontendConfig
}from"./auth-config";

describe("frontend auth config",()=>{
  it("defaults to Demo",()=>{
    expect(
      normalizeFrontendAuthMode(undefined)
    ).toBe("Demo");
  });

  it("normalizes Demo and Entra",()=>{
    expect(
      normalizeFrontendAuthMode(" demo ")
    ).toBe("Demo");

    expect(
      normalizeFrontendAuthMode("ENTRA")
    ).toBe("Entra");
  });

  it("rejects unknown mode",()=>{
    expect(()=>
      normalizeFrontendAuthMode("Hybrid")
    ).toThrow();
  });

  it("requires Entra SPA settings",()=>{
    expect(()=>
      validateEntraFrontendConfig({
        tenantId:"",
        clientId:"",
        apiScope:"",
        redirectUri:""
      })
    ).toThrow();
  });

  it("rejects SPA home page, insecure and malformed popup redirect URIs",()=>{
    const base={
      tenantId:"11111111-1111-1111-1111-111111111111",
      clientId:"22222222-2222-2222-2222-222222222222",
      apiScope:"api://33333333-3333-3333-3333-333333333333/access_as_user"
    };
    for(const redirectUri of [
      "https://example.test/app/",
      "http://example.test/redirect.html",
      "https://example.test/app/redirect.html#token",
      "https://example.test/app/redirect.html?unsafe=yes",
      "https://name:password@example.test/redirect.html",
      "javascript:alert(1)",
      ""
    ]){
      expect(()=>
        validateEntraFrontendConfig({...base,redirectUri})
      ).toThrow();
    }
    expect(()=>
      validateEntraFrontendConfig({...base,redirectUri:"http://localhost:5173/redirect.html"})
    ).not.toThrow();
  });

  it("accepts complete Entra SPA settings",()=>{
    expect(()=>
      validateEntraFrontendConfig({
        tenantId:
          "11111111-1111-1111-1111-111111111111",
        clientId:
          "22222222-2222-2222-2222-222222222222",
        apiScope:
          "api://33333333-3333-3333-3333-333333333333/access_as_user",
        redirectUri:
          "https://example.test/app/redirect.html"
      })
    ).not.toThrow();
  });
});
