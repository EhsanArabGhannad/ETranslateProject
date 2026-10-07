#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [switch]$EmitBrowserFixture
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Verify-WebWorkspace.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl
$baselineChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
$teamPath = "$IdentityUrl/api/v1/tenants/$tenantId/team"
$reviewApi = "$documentPath/$($document.id)"
function Register-TestUser([string]$Name) {
    $address = "team-$Name-$runId@etranslate.local"; $secret = "Team-$runId!"
    Api POST "$IdentityUrl/api/v1/auth/register" @{ email = $address; password = $secret } 200 @{} | Out-Null
    $login = Api POST "$IdentityUrl/api/v1/auth/login" @{ email = $address; password = $secret } 200 @{}
    return @{ Email = $address; Password = $secret; Headers = @{ Authorization = "Bearer $($login.accessToken)" } }
}
function State($Headers = $bearer) { Api GET $teamPath $null 200 $Headers }
function Invite($Account, [string]$Role, [int]$Status = 201, $Headers = $bearer) {
    $state = State $Headers
    Api POST "$teamPath/invitations" @{ email = $Account.Email; role = $Role; expectedTeamVersion = $state.teamVersion } $Status $Headers
}
function Accept($Invitation, $Account, [int]$Status = 200, [string]$Token = $Invitation.token) {
    Api POST "$IdentityUrl/api/v1/team-invitations/$($Invitation.id)/accept" @{ token = $Token } $Status $Account.Headers
}
function Role([string]$UserId, [string]$NewRole, [int]$Status = 200, $Headers = $bearer) {
    $state = State $Headers
    Api PUT "$teamPath/members/$UserId/role" @{ role = $NewRole; expectedTeamVersion = $state.teamVersion } $Status $Headers
}
function Active([string]$UserId, [bool]$Value, [int]$Status = 200, $Headers = $bearer) {
    $state = State $Headers
    Api PUT "$teamPath/members/$UserId/status" @{ isActive = $Value; expectedTeamVersion = $state.teamVersion } $Status $Headers
}
function Cancel($Invitation, [int]$Status = 200, $Headers = $bearer) {
    $state = State $Headers
    Api POST "$teamPath/invitations/$($Invitation.id)/cancel" @{ expectedTeamVersion = $state.teamVersion } $Status $Headers
}
function Race([string]$Url, $Body, $Headers, [int[]]$Expected) {
    $requests = @(); $tasks = @(); $results = @()
    try {
        for ($index = 0; $index -lt 2; $index++) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, $Url)
            $request.Headers.TryAddWithoutValidation('Authorization', $Headers.Authorization) | Out-Null
            $request.Content = [Net.Http.StringContent]::new((ConvertTo-Json $Body -Depth 10), [Text.Encoding]::UTF8, 'application/json')
            $requests += $request; $tasks += $http.SendAsync($request)
        }
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $results += @{ Status = [int]$response.StatusCode; Body = if ($body) { ConvertFrom-Json $body } else { $null } }
            } finally { $response.Dispose() }
        }
        $statuses = @($results.Status | Sort-Object)
        Assert-Check (($statuses -join ',') -eq ($Expected -join ',')) "Concurrent request statuses: $statuses; expected $Expected"
        return ($results | Where-Object Status -eq $Expected[0] | Select-Object -First 1).Body
    } finally { foreach ($request in $requests) { $request.Dispose() } }
}
try {
    $translator = Register-TestUser 'translator'; $admin = Register-TestUser 'admin'
    $observer = Register-TestUser 'observer'; $guest = Register-TestUser 'guest'; $concurrent = Register-TestUser 'concurrent'
    $state = State; $ownerId = $state.userId
    Assert-Check ($state.members.Count -eq 1 -and $state.members[0].isActive -and $state.members[0].role -eq 'Owner' -and -not $state.members[0].canEdit) 'Existing owner must remain active and protected'
    Api GET $teamPath $null 401 @{} | Out-Null
    Api GET $teamPath $null 403 $guest.Headers | Out-Null
    Role $ownerId 'Translator' 403 | Out-Null
    Active $ownerId $false 403 | Out-Null
    Invite $guest 'Owner' 403 | Out-Null
    Invite @{ Email = "not-registered-$runId@etranslate.local" } 'Translator' 400 | Out-Null
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET "/Workspace?tenantId=$tenantId" 200
    Assert-Check ($page.Body -match '/Team') 'Owner must have team management link'
    $teamPage = Web GET "/Team?tenantId=$tenantId" 200
    $inviteFields = @{ tenantId = $tenantId; email = $translator.Email; role = 'Translator'; expectedTeamVersion = (State).teamVersion }
    Web POST '/Team/Invite' 400 (Fields $inviteFields) | Out-Null
    $inviteFields.__RequestVerificationToken = Csrf $teamPage.Body
    $createdPage = Web POST '/Team/Invite' 200 (Fields $inviteFields)
    $tokenMatch = [regex]::Match($createdPage.Body, 'id="invitation-token"[^>]*value="([^"]+)"')
    $linkMatch = [regex]::Match($createdPage.Body, 'id="invitation-link"[^>]*value="([^"]+)"')
    Assert-Check ($tokenMatch.Success -and $linkMatch.Success) 'Creation must show one-time manual link/token'
    $token = [Net.WebUtility]::HtmlDecode($tokenMatch.Groups[1].Value)
    $link = [Net.WebUtility]::HtmlDecode($linkMatch.Groups[1].Value)
    Assert-Check ($link -match '#[A-Za-z0-9_-]{43}$' -and $link -notmatch 'token=') 'Token must be in a fragment, not a logged query string'
    $state = State; $translatorInvitation = $state.invitations | Where-Object email -eq $translator.Email
    Assert-Check ($state.members.Count -eq 1) 'Invitation must not create membership before consent'
    Assert-Check ((ConvertTo-Json $state -Depth 10) -notmatch 'TokenHash|"token"') 'Team listing must never reveal secrets'
    $translatorInvitation = [pscustomobject]@{ id = $translatorInvitation.id; targetUserId = $translatorInvitation.targetUserId; token = $token }
    Invite $translator 'Translator' 409 | Out-Null
    Api GET "$IdentityUrl/api/v1/team-invitations/$($translatorInvitation.id)" $null 404 $guest.Headers | Out-Null
    Accept $translatorInvitation $guest 404 | Out-Null
    Accept $translatorInvitation $translator 404 (('a' * 43) -join '') | Out-Null
    Api GET "$IdentityUrl/api/v1/team-invitations/$($translatorInvitation.id)" $null 401 @{} | Out-Null
    $preview = Api GET "$IdentityUrl/api/v1/team-invitations/$($translatorInvitation.id)" $null 200 $translator.Headers
    Assert-Check ($preview.canAccept -and $preview.role -eq 'Translator') 'Only intended account can preview invitation'
    Accept $translatorInvitation $translator | Out-Null
    Accept $translatorInvitation $translator 409 | Out-Null
    Api GET "$IdentityUrl/api/v1/tenants/$tenantId/access" $null 200 $translator.Headers | Out-Null
    Api GET $teamPath $null 403 $translator.Headers | Out-Null
    $translatorId = $translatorInvitation.targetUserId
    $adminInvitation = Invite $admin 'Administrator'; Accept $adminInvitation $admin | Out-Null
    $adminId = $adminInvitation.targetUserId
    $observerInvitation = Invite $observer 'Reviewer' 201 $admin.Headers; Accept $observerInvitation $observer | Out-Null
    $observerId = $observerInvitation.targetUserId
    Invite $guest 'Administrator' 403 $admin.Headers | Out-Null
    Role $adminId 'Translator' 403 $admin.Headers | Out-Null
    Role $ownerId 'Reviewer' 403 $admin.Headers | Out-Null
    Role $translatorId 'Administrator' 403 $admin.Headers | Out-Null
    Api POST "$teamPath/invitations" @{ email = $guest.Email; role = 'Translator'; expectedTeamVersion = (State).teamVersion } 403 $translator.Headers | Out-Null
    Api GET $teamPath $null 403 $observer.Headers | Out-Null
    Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 2; editorContentJson = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"ترجمه توسط مترجم تیم"}]}]}' } 403 $observer.Headers | Out-Null
    $draft = Api POST "$reviewApi/draft-revisions" @{ expectedCurrentRevision = 2; editorContentJson = '{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"ترجمه توسط مترجم تیم"}]}]}' } 201 $translator.Headers
    Assert-Check ($draft.createdByUserId -eq $translatorId) 'Draft must record actual translator account'
    $pdf = Api POST "$reviewApi/draft-revisions/3/pdfs" $null 201 $translator.Headers
    $review = Api POST "$reviewApi/reviews" @{ expectedRevision = 3; expectedRound = 0; pdfVersionId = $pdf.id } 201 $translator.Headers
    Api POST "$reviewApi/reviews/$($review.id)/decision" @{ action = 'Approve' } 403 $translator.Headers | Out-Null
    Api POST "$reviewApi/reviews/$($review.id)/decision" @{ action = 'Approve' } 403 $observer.Headers | Out-Null
    $approved = Api POST "$reviewApi/reviews/$($review.id)/decision" @{ action = 'Approve'; note = 'Reviewed by separate office manager' } 200 $admin.Headers
    Assert-Check ($approved.submittedByUserId -eq $translatorId -and $approved.decidedByUserId -eq $adminId -and $translatorId -ne $adminId) 'Translation/review must work with separate identities'
    $version = (State).teamVersion
    Role $translatorId 'Reviewer' 200 $admin.Headers | Out-Null
    Api PUT "$teamPath/members/$translatorId/role" @{ role = 'Translator'; expectedTeamVersion = $version } 409 | Out-Null
    Api POST "$reviewApi/reviews/$($review.id)/decision" @{ action = 'Reopen'; note = 'Denied after role change' } 403 $translator.Headers | Out-Null
    Role $translatorId 'Translator' 200 $admin.Headers | Out-Null
    Active $translatorId $false 200 $admin.Headers | Out-Null
    Api GET "$IdentityUrl/api/v1/tenants/$tenantId/access" $null 404 $translator.Headers | Out-Null
    Api GET $documentPath $null 403 $translator.Headers | Out-Null
    Api GET "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs/$jobId" $null 403 $translator.Headers | Out-Null
    Assert-Check (@(Api GET "$IdentityUrl/api/v1/tenants" $null 200 $translator.Headers).Count -eq 0) 'Inactive tenant must disappear from tenant listing'
    $after = State; $member = $after.members | Where-Object userId -eq $translatorId
    Assert-Check (-not $member.isActive -and $member.userId -eq $translatorId) 'Deactivation must preserve identity/history rather than delete it'
    Active $translatorId $true | Out-Null
    Api GET $documentPath $null 200 $translator.Headers | Out-Null
    Api PUT "$teamPath/members/$translatorId/status" @{ expectedTeamVersion = (State).teamVersion } 400 | Out-Null
    $cancelled = Invite $guest 'Translator'; Cancel $cancelled | Out-Null; Accept $cancelled $guest 409 | Out-Null
    $staleAuthority = Invite $guest 'Translator' 201 $admin.Headers
    Active $adminId $false | Out-Null
    Accept $staleAuthority $guest 409 | Out-Null
    Cancel $staleAuthority | Out-Null
    Active $adminId $true | Out-Null
    $version = (State).teamVersion
    $racedInvitation = Race "$teamPath/invitations" @{ email = $concurrent.Email; role = 'Translator'; expectedTeamVersion = $version } $bearer @(201,409)
    Race "$IdentityUrl/api/v1/team-invitations/$($racedInvitation.id)/accept" @{ token = $racedInvitation.token } $concurrent.Headers @(200,409) | Out-Null
    $state = State
    Assert-Check (@($state.members | Where-Object email -eq $concurrent.Email).Count -eq 1) 'Concurrent acceptance must not duplicate membership'
    Api GET "$IdentityUrl/api/v1/tenants/$($other.id)/team" $null 403 $admin.Headers | Out-Null
    Api PUT "$IdentityUrl/api/v1/tenants/$($other.id)/team/members/$translatorId/role" @{ role = 'Reviewer'; expectedTeamVersion = 0 } 404 | Out-Null
    $state = State
    Assert-Check ($state.audit.Count -ge 10 -and $state.audit[0].teamVersion -eq $state.teamVersion) 'Team changes must have ordered audit records'
    $teamPage = Web GET "/Team?tenantId=$tenantId" 200
    Assert-Check ($teamPage.Body -notmatch [regex]::Escape($token) -and $teamPage.Body -notmatch 'id="invitation-token"') 'Reloaded team must not reveal invitation token again'
    Web POST '/Team/Role' 400 (Fields @{ tenantId = $tenantId; userId = $translatorId; role = 'Reviewer'; expectedTeamVersion = $state.teamVersion }) | Out-Null
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $teamPage.Body) }) | Out-Null
    Web GET "/Team?tenantId=$tenantId" 302 | Out-Null
    Write-Output "PASS: $($checks - $baselineChecks) team checks plus $baselineChecks baseline checks. Development tenant: $tenantId"
    if ($EmitBrowserFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; tenantId = $tenantId; jobId = $jobId; guestEmail = $guest.Email; guestPassword = $guest.Password; translatorEmail = $translator.Email; adminEmail = $admin.Email } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
