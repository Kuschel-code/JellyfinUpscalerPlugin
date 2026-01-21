# Spec and build

## Configuration
- **Artifacts Path**: {@artifacts_path} → `.zenflow/tasks/{task_id}`

---

## Agent Instructions

Ask the user questions when anything is unclear or needs their input. This includes:
- Ambiguous or incomplete requirements
- Technical decisions that affect architecture or user experience
- Trade-offs that require business context

Do not make assumptions on important decisions — get clarification first.

---

## Workflow Steps

### [x] Step: Technical Specification

**Completed**: Technical specification created at `.zenflow/tasks/jellyfin-plugin-ba45/spec.md`  
**Complexity**: HARD  
**Estimated Time**: 16-23 hours

---

### [x] Step: Setup Development Environment
<!-- chat-id: 857cd1ed-6a41-420d-9c9d-202fd2b120a9 -->

**Goal**: Prepare development environment for Jellyfin plugin development

**Tasks**:
- Clone/download Jellyfin plugin template from GitHub
- Install .NET SDK 6.0+ (if not already installed)
- Setup project structure and dependencies
- Configure build tools and test basic compilation
- Create `.gitignore` for C#/Jellyfin projects

**Verification**:
- [x] `dotnet --version` shows .NET 6.0 or higher
- [x] Project builds successfully with `dotnet build`
- [x] No compilation errors

---

### [x] Step: Implement Backend Core Services
<!-- chat-id: dfd31db5-1be2-408f-9744-393b2215d14c -->

**Goal**: Create C# plugin foundation with media stream analysis capabilities

**Files to Create**:
- `Jellyfin.Plugin.LanguageSelector/Jellyfin.Plugin.LanguageSelector.csproj`
- `Jellyfin.Plugin.LanguageSelector/Plugin.cs`
- `Jellyfin.Plugin.LanguageSelector/Configuration/PluginConfiguration.cs`
- `Jellyfin.Plugin.LanguageSelector/Models/LanguageOption.cs`
- `Jellyfin.Plugin.LanguageSelector/Models/MediaStreamInfo.cs`
- `Jellyfin.Plugin.LanguageSelector/Services/MediaStreamAnalyzer.cs`
- `Jellyfin.Plugin.LanguageSelector/Services/LanguageDetector.cs`

**Key Functionality**:
- Implement `MediaStreamAnalyzer` to extract audio/subtitle tracks from media files
- Implement `LanguageDetector` to map language codes (ger/deu/jpn/eng) to flag icons
- Create data models for language options
- Setup plugin configuration and metadata

**Verification**:
- [x] Project builds without errors
- [x] Services can be instantiated and basic methods work
- [x] Language detection correctly maps ISO codes (test with unit tests if time permits)

---

### [x] Step: Implement API Controller
<!-- chat-id: 645680bf-e112-4c9f-a91d-12da3531c9fa -->

**Goal**: Create REST API endpoint to expose language options to frontend

**Files to Create**:
- `Jellyfin.Plugin.LanguageSelector/Api/LanguageOptionsController.cs`

**Key Functionality**:
- Implement `GET /Items/{itemId}/LanguageOptions` endpoint
- Integrate with `MediaStreamAnalyzer` and `LanguageDetector`
- Return JSON response with available language combinations
- Handle edge cases (no audio/subtitle tracks, single language, etc.)

**Verification**:
- [x] Plugin builds and deploys to Jellyfin server
- [ ] Plugin appears in Jellyfin Admin Dashboard
- [ ] API endpoint accessible via curl/Postman
- [ ] Correct JSON response for test media files with multiple audio/subtitle tracks

**Test Command**:
```bash
curl http://localhost:8096/Items/{itemId}/LanguageOptions
```

---

### [ ] Step: Implement Frontend UI Component

**Goal**: Create JavaScript component to render flag buttons and handle playback

**Files to Create**:
- `Jellyfin.Plugin.LanguageSelector/Web/language-selector.js`
- `Jellyfin.Plugin.LanguageSelector/Web/language-selector.css`
- `Jellyfin.Plugin.LanguageSelector/Web/flags/` (flag icon SVGs: de.svg, jp.svg, us.svg, jp-de.svg, jp-us.svg)

**Key Functionality**:
- Detect when user navigates to episode detail page
- Fetch language options from API endpoint
- Render flag buttons with AniWorld-inspired design
- Attach click handlers to start playback with specific audio/subtitle tracks
- Implement DOM injection logic with mutation observer for robustness

**Verification**:
- [ ] JavaScript loads in browser console without errors
- [ ] Flag buttons appear on episode detail page
- [ ] CSS styling matches AniWorld reference (blue bar, rounded corners, hover effects)
- [ ] Clicking flag logs correct audio/subtitle indices (test in console first)

---

### [ ] Step: Integrate Playback Control

**Goal**: Connect flag buttons to Jellyfin's playback API for one-click playback

**Key Functionality**:
- Integrate with Jellyfin's `PlaybackManager` API
- Implement `startPlaybackWithTracks()` method
- Pass `audioStreamIndex` and `subtitleStreamIndex` to playback API
- Handle playback errors and edge cases

**Verification**:
- [ ] Clicking German flag starts playback with German audio, no subtitles
- [ ] Clicking JP/DE flag starts playback with Japanese audio + German subtitles
- [ ] Clicking JP/EN flag starts playback with Japanese audio + English subtitles
- [ ] No manual track selection needed after clicking flag
- [ ] Playback starts immediately without delays

**Test Files Needed**:
- Anime episode with multiple audio/subtitle tracks (ger, jpn, eng combinations)

---

### [ ] Step: End-to-End Testing & Bug Fixes

**Goal**: Test complete workflow and fix any issues

**Testing Checklist**:
- [ ] Test with anime episodes (primary use case)
- [ ] Test with regular movies/shows (general compatibility)
- [ ] Test with edge cases:
  - Files with only one audio track
  - Files with no subtitles
  - Files with >3 language combinations
  - Files with forced subtitles
- [ ] Test on Chrome and Firefox browsers
- [ ] Test UI responsiveness (mobile/tablet if applicable)
- [ ] Verify progress tracking and "watched" status work correctly

**Bug Fixes**:
- Address any issues found during testing
- Refine UI/UX based on user experience
- Optimize performance if needed

---

### [ ] Step: Documentation & Packaging

**Goal**: Prepare plugin for distribution and document installation/usage

**Tasks**:
- Create `README.md` with:
  - Plugin description and features
  - Installation instructions (manual & repository)
  - Build instructions for developers
  - Usage guide with screenshots
  - Troubleshooting section
- Create `build.yaml` for JPRM (Jellyfin Plugin Repository Manager)
- Test build and packaging process
- Create GitHub release with pre-built DLL

**Verification**:
- [ ] Plugin can be built with standard `dotnet build` command
- [ ] Installation instructions are clear and accurate
- [ ] Plugin works after fresh installation on clean Jellyfin instance

---

### [ ] Step: Final Report

**Goal**: Document implementation and outcomes

**Tasks**:
- Write report to `.zenflow/tasks/jellyfin-plugin-ba45/report.md`
- Document what was implemented
- Describe testing approach and results
- Note any challenges encountered and how they were resolved
- List any known limitations or future improvements
