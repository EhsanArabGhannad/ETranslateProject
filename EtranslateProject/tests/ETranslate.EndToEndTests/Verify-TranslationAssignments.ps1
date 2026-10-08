#Requires -Version 7.0
param(
    [string]$WebUrl = 'https://localhost:7049',
    [string]$IdentityUrl = 'http://localhost:5031',
    [string]$WorkflowUrl = 'http://localhost:5285',
    [string]$DocumentsUrl = 'http://localhost:5241',
    [switch]$EmitBrowserFixture
)
$ErrorActionPreference = 'Stop'
$emitAssignmentBrowserFixture = $EmitBrowserFixture
. "$PSScriptRoot/Verify-TenantTeam.ps1" -WebUrl $WebUrl -IdentityUrl $IdentityUrl -WorkflowUrl $WorkflowUrl -DocumentsUrl $DocumentsUrl
$priorChecks = $checks
$handler = [Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler); $client.Timeout = [TimeSpan]::FromSeconds(45)
$http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(45)
$jobsApi = "$WorkflowUrl/api/v1/tenants/$tenantId/translation-jobs"
$assignmentApi = "$jobsApi/$jobId/assignment"
$candidatesApi = "$WorkflowUrl/api/v1/tenants/$tenantId/translation-assignees"
function Assignment-State($Headers = $bearer) { Api GET $assignmentApi $null 200 $Headers }
function Set-Assignment($Target, [int]$Status = 200, $Headers = $bearer, [string]$Note = 'Development test assignment') {
    $assignment = Assignment-State $Headers
    Api PUT $assignmentApi @{ translatorUserId = $Target; expectedAssignmentVersion = $assignment.assignmentVersion; note = $Note } $Status $Headers
}
function Race-Assignment($Target, [long]$Version) {
    $requests = @(); $tasks = @(); $results = @()
    try {
        for ($index = 0; $index -lt 2; $index++) {
            $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Put, $assignmentApi)
            $request.Headers.TryAddWithoutValidation('Authorization', $bearer.Authorization) | Out-Null
            $request.Content = [Net.Http.StringContent]::new((ConvertTo-Json @{ translatorUserId = $Target; expectedAssignmentVersion = $Version }), [Text.Encoding]::UTF8, 'application/json')
            $requests += $request; $tasks += $http.SendAsync($request)
        }
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try { $results += [int]$response.StatusCode } finally { $response.Dispose() }
        }
        Assert-Check ((@($results | Sort-Object) -join ',') -eq '200,409') 'Concurrent assignments must result in exactly one success and one conflict'
    } finally { foreach ($request in $requests) { $request.Dispose() } }
}
try {
    $state = Assignment-State
    Assert-Check ($null -eq $state.assignedTranslatorUserId -and $state.assignmentVersion -eq 0 -and $state.history.Count -eq 0) 'Existing jobs must start unassigned with empty history'
    Api GET $assignmentApi $null 401 @{} | Out-Null
    Api GET $assignmentApi $null 403 $guest.Headers | Out-Null
    Api GET "$jobsApi/$([Guid]::NewGuid())/assignment" $null 404 | Out-Null
    Api GET "$WorkflowUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/assignment" $null 404 | Out-Null
    Api PUT "$WorkflowUrl/api/v1/tenants/$($other.id)/translation-jobs/$jobId/assignment" @{ translatorUserId = $ownerId; expectedAssignmentVersion = 0 } 404 | Out-Null
    $candidates = @(Api GET $candidatesApi $null 200)
    Assert-Check (@($candidates | Where-Object userId -eq $translatorId).Count -eq 1) 'Translator must be a candidate'
    Assert-Check (@($candidates | Where-Object userId -eq $ownerId).Count -eq 1) 'Owner may do translation work'
    Assert-Check (@($candidates | Where-Object userId -eq $observerId).Count -eq 0) 'Read-only observer must not be a candidate'
    Api GET $candidatesApi $null 403 $translator.Headers | Out-Null
    Api GET "$IdentityUrl/api/v1/tenants/$tenantId/translation-assignees" $null 403 $observer.Headers | Out-Null
    Set-Assignment $observerId 400 | Out-Null
    Set-Assignment $([Guid]::NewGuid()) 400 | Out-Null
    Set-Assignment ([Guid]::Empty) 400 | Out-Null
    Set-Assignment $translatorId 400 $bearer ('x' * 501) | Out-Null
    Set-Assignment $translatorId 400 $bearer (' ' * 501) | Out-Null
    $outsider = Register-TestUser 'outside-assignee'
    $outsiderLogin = Api GET "$IdentityUrl/api/v1/tenants" $null 200 $outsider.Headers
    $outsiderTenant = Api POST "$IdentityUrl/api/v1/tenants" @{ name = 'External office'; slug = "outside-$runId"; type = 'TranslationOffice' } 201 $outsider.Headers
    $outsiderAccess = Api GET "$IdentityUrl/api/v1/tenants/$($outsiderTenant.id)/access" $null 200 $outsider.Headers
    Set-Assignment $outsiderAccess.userId 400 | Out-Null
    Api PUT $assignmentApi @{ translatorUserId = $ownerId; expectedAssignmentVersion = 0 } 403 $translator.Headers | Out-Null
    Api PUT $assignmentApi @{ translatorUserId = $ownerId; expectedAssignmentVersion = 0 } 403 $observer.Headers | Out-Null
    $originalJob = Api GET "$jobsApi/$jobId" $null 200
    $originalReview = Api GET "$reviewApi/reviews" $null 200
    $assigned = Set-Assignment $translatorId
    Assert-Check ($assigned.assignedTranslatorUserId -eq $translatorId -and $assigned.assignmentVersion -eq 1 -and $assigned.assignmentChangedByUserId -eq $ownerId) 'Owner assignment must record actual actor and version'
    $mine = @(Api GET "$jobsApi`?assignedToMe=true" $null 200 $translator.Headers)
    Assert-Check ($mine.Count -eq 1 -and $mine[0].id -eq $jobId) 'My tasks must filter using authenticated user ID'
    Assert-Check (@(Api GET "$jobsApi`?assignedToMe=true&userId=$translatorId" $null 200 $admin.Headers).Count -eq 0) 'Client-supplied user IDs must not impersonate another member in My tasks'
    Assert-Check (@(Api GET "$jobsApi`?assignedToMe=true" $null 200 $admin.Headers).Count -eq 0) 'Other member must not receive translator tasks in My tasks'
    Api GET "$jobsApi/$jobId" $null 200 $admin.Headers | Out-Null
    Assert-Check (@(Api GET $jobsApi $null 200 $observer.Headers).Count -ge 2) 'Assignment is not a per-job visibility restriction'
    Set-Assignment $translatorId 409 | Out-Null
    Api PUT $assignmentApi @{ translatorUserId = $adminId; expectedAssignmentVersion = 0 } 409 | Out-Null
    Set-Assignment $adminId 200 $admin.Headers | Out-Null
    Assert-Check (@(Api GET "$jobsApi`?assignedToMe=true" $null 200 $translator.Headers).Count -eq 0) 'Reassignment removes the job from previous translator tasks'
    $state = Assignment-State
    Assert-Check ($state.history.Count -eq 2 -and $state.history[0].previousTranslatorUserId -eq $translatorId -and $state.history[0].actorUserId -eq $adminId) 'Reassignment history must preserve prior translator and manager'
    Set-Assignment $null | Out-Null
    Assert-Check ($null -eq (Assignment-State).assignedTranslatorUserId) 'Explicit unassignment must clear the responsible translator'
    Set-Assignment $ownerId | Out-Null
    Assert-Check (@(Api GET "$jobsApi`?assignedToMe=true" $null 200).Count -eq 1) 'Independent/office owner may be assigned translation work'
    Active $translatorId $false | Out-Null
    Set-Assignment $translatorId 400 | Out-Null
    Active $translatorId $true | Out-Null
    Role $translatorId 'Reviewer' | Out-Null
    Set-Assignment $translatorId 400 | Out-Null
    Role $translatorId 'Translator' | Out-Null
    Race-Assignment $translatorId (Assignment-State).assignmentVersion
    $state = Assignment-State
    Assert-Check ($state.assignmentVersion -eq 5 -and $state.history.Count -eq 5) 'Failed/stale/racing assignments must not add extra audit rows'
    Active $translatorId $false | Out-Null
    Assert-Check ((Assignment-State).assignedTranslatorUserId -eq $translatorId) 'Deactivation must preserve assignment history'
    Api GET "$jobsApi`?assignedToMe=true" $null 403 $translator.Headers | Out-Null
    $candidates = @(Api GET $candidatesApi $null 200)
    Assert-Check (@($candidates | Where-Object userId -eq $translatorId).Count -eq 0) 'Inactive current assignee must be removed from new candidates'
    Active $translatorId $true | Out-Null
    $currentJob = Api GET "$jobsApi/$jobId" $null 200
    $currentReview = Api GET "$reviewApi/reviews" $null 200
    Assert-Check ($currentJob.createdByUserId -eq $originalJob.createdByUserId -and $currentJob.status -eq $originalJob.status -and $currentJob.signaturePolicy -eq $originalJob.signaturePolicy) 'Assignment must not rewrite creation/legal policy'
    Assert-Check ((ConvertTo-Json $currentReview -Depth 15 -Compress) -eq (ConvertTo-Json $originalReview -Depth 15 -Compress)) 'Assignment must not rewrite document review or PDF history'
    $loginPage = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $email; password = $password; __RequestVerificationToken = (Csrf $loginPage.Body) }) | Out-Null
    $page = Web GET "/Workspace/Assignment?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($page.Body -match 'assigned-translator' -and $page.Body -match [regex]::Escape($translator.Email)) 'Manager assignment page must show active translator choices'
    $fields = @{ tenantId = $tenantId; jobId = $jobId; translatorUserId = $adminId; expectedAssignmentVersion = $state.assignmentVersion }
    Web POST '/Workspace/AssignTranslator' 400 (Fields $fields) | Out-Null
    $fields.__RequestVerificationToken = Csrf $page.Body
    Web POST '/Workspace/AssignTranslator' 302 (Fields $fields) | Out-Null
    Assert-Check ((Assignment-State).assignedTranslatorUserId -eq $adminId) 'Web assignment form must update the actual assignment'
    $page = Web GET "/Workspace/Assignment?tenantId=$tenantId&jobId=$jobId" 200
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    $page = Web GET '/Account/Login' 200
    Web POST '/Account/Login' 302 (Fields @{ email = $translator.Email; password = $translator.Password; __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    $page = Web GET "/Workspace/Assignment?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($page.Body -notmatch 'id="assigned-translator"') 'Translator must see read-only assignment history'
    $fields = @{ tenantId = $tenantId; jobId = $jobId; translatorUserId = $translatorId; expectedAssignmentVersion = (Assignment-State).assignmentVersion; __RequestVerificationToken = (Csrf $page.Body) }
    Web POST '/Workspace/AssignTranslator' 302 (Fields $fields) | Out-Null
    Assert-Check ((Assignment-State).assignedTranslatorUserId -eq $adminId) 'Forged Web form must not let translator self-assign'
    $page = Web GET "/Workspace?tenantId=$tenantId&assignedToMe=true" 200
    Assert-Check ($page.Body -notmatch "jobId=$jobId") 'Web My tasks must not show a job assigned to another member'
    Set-Assignment $translatorId | Out-Null
    $page = Web GET "/Workspace?tenantId=$tenantId&assignedToMe=true" 200
    Assert-Check ($page.Body -match "jobId=$jobId") 'Web My tasks must show a newly assigned job'
    $page = Web GET "/Workspace/Editor?tenantId=$tenantId&jobId=$jobId" 200
    Assert-Check ($page.Body -match '/Workspace/Assignment' -and $page.Body -match 'target="_blank"') 'Assignment must open separately from unsaved editor input'
    Web POST '/Account/Logout' 302 (Fields @{ __RequestVerificationToken = (Csrf $page.Body) }) | Out-Null
    Write-Output "PASS: $($checks - $priorChecks) assignment checks plus $priorChecks team/baseline checks. Development tenant: $tenantId"
    if ($emitAssignmentBrowserFixture) { Write-Output (ConvertTo-Json @{ email = $email; password = $password; translatorEmail = $translator.Email; translatorPassword = $translator.Password; tenantId = $tenantId; jobId = $jobId; translatorId = $translatorId; adminId = $adminId } -Compress) }
} finally { $http.Dispose(); $client.Dispose(); $handler.Dispose() }
