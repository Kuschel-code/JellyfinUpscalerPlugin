# Testing Guide - Jellyfin Language Selector Plugin

## Pre-Testing Setup

### 1. Build the Plugin
```bash
dotnet build "C:\Users\Kuscheltier\.zenflow\worktrees\jellyfin-plugin-ba45\Jellyfin.Plugin.LanguageSelector\Jellyfin.Plugin.LanguageSelector.csproj" --configuration Release
```

### 2. Installation
1. Locate the built DLL: `bin\Release\net6.0\Jellyfin.Plugin.LanguageSelector.dll`
2. Copy to Jellyfin plugin directory: `%AppData%\Jellyfin\Server\plugins\LanguageSelector\`
3. Restart Jellyfin Server
4. Verify plugin appears in Admin Dashboard → Plugins

### 3. Test Media Files Needed
Prepare at least these types of files in your Jellyfin library:
- **Test 1**: Anime with German audio only
- **Test 2**: Anime with Japanese audio + German subtitles
- **Test 3**: Anime with Japanese audio + German & English subtitles
- **Test 4**: Anime with German & Japanese audio + multiple subtitles
- **Test 5**: Regular movie (non-anime) with multiple languages
- **Test 6**: Episode with only one audio track (no subtitles)
- **Test 7**: Episode with forced subtitles

---

## Testing Checklist

### Phase 1: Backend API Testing

#### Test 1.1: Plugin Installation
- [ ] Plugin appears in Admin Dashboard
- [ ] Plugin shows correct name: "Language Selector"
- [ ] Plugin shows correct version
- [ ] No errors in Jellyfin server logs

#### Test 1.2: API Endpoint Availability
Test the API endpoint using curl or browser:

```bash
# Replace {itemId} with an actual episode ID from your library
# Get item ID from: http://localhost:8096/web/index.html#!/item?id={itemId}

curl -H "X-Emby-Token: YOUR_API_KEY" http://localhost:8096/Items/{itemId}/LanguageOptions
```

**Expected Response**:
```json
{
  "options": [
    {
      "id": "de",
      "displayName": "German",
      "flagIcon": "de",
      "audioStreamIndex": 0,
      "subtitleStreamIndex": null,
      "audioLanguage": "ger",
      "subtitleLanguage": null,
      "isDefault": true
    }
  ],
  "itemId": "{itemId}",
  "itemName": "Episode Name"
}
```

**Checklist**:
- [ ] Endpoint returns HTTP 200 OK
- [ ] Response contains `options` array
- [ ] Each option has all required fields
- [ ] `audioStreamIndex` matches actual file stream indices
- [ ] `subtitleStreamIndex` is `null` or valid number (not -1 in JSON)
- [ ] Language codes are correctly normalized (ger→de, jpn→jp, eng→us)

#### Test 1.3: Language Detection Logic
Test with different media files:

- [ ] **German audio only**: Returns 1 option (de flag)
- [ ] **Japanese audio + German subs**: Returns 2 options (jp flag, jp-de flag)
- [ ] **Japanese audio + German & English subs**: Returns 3 options (jp, jp-de, jp-us)
- [ ] **Multiple audio tracks**: Returns options for each audio track
- [ ] **Forced subtitles**: Forced subtitles are excluded from main options
- [ ] **No audio tracks**: Returns empty options array (no crash)

#### Test 1.4: Edge Cases
- [ ] File with no language metadata → Should still work with "unknown" language
- [ ] File with malformed language codes → Should normalize or fallback gracefully
- [ ] Non-video items (music, photos) → Should return 404 or empty options
- [ ] Item that doesn't exist → Returns HTTP 404

---

### Phase 2: Frontend UI Testing

#### Test 2.1: Flag Button Rendering
1. Navigate to an episode detail page in Jellyfin web UI
2. Open browser console (F12)
3. Check for JavaScript errors

**Checklist**:
- [ ] No JavaScript errors in console
- [ ] Flag buttons appear on the page (below/near play button)
- [ ] Flag icons are displayed correctly (not broken images)
- [ ] Correct number of flags matches available language options
- [ ] Hover effects work (border appears on hover)
- [ ] Buttons have appropriate cursor (pointer)

#### Test 2.2: UI Styling
Compare with the AniWorld screenshot:

- [ ] Buttons have blue background bar (#5B7FBF)
- [ ] Rounded corners (border-radius: 8px)
- [ ] Proper spacing between flags
- [ ] Flags are appropriately sized (not too small/large)
- [ ] Hover effect shows semi-transparent white border
- [ ] Loading state: buttons are disabled and show opacity change

#### Test 2.3: Flag Icon Display
- [ ] German flag (DE) displays correctly
- [ ] Japanese + German flags (JP/DE) display correctly
- [ ] Japanese + English flags (JP/US) display correctly
- [ ] Japanese flag (JP) displays correctly
- [ ] English flag (US) displays correctly

---

### Phase 3: Playback Integration Testing

#### Test 3.1: One-Click Playback - German Audio
1. Navigate to episode with German audio
2. Click German flag button
3. Video should start immediately

**Checklist**:
- [ ] Video starts playing without additional clicks
- [ ] Audio track is German
- [ ] No subtitles are active
- [ ] Progress tracking works correctly
- [ ] Can pause/resume normally

#### Test 3.2: One-Click Playback - Japanese + German Subs
1. Navigate to episode with Japanese audio + German subs
2. Click JP/DE flag button
3. Video should start immediately

**Checklist**:
- [ ] Video starts playing without additional clicks
- [ ] Audio track is Japanese
- [ ] German subtitles are active and visible
- [ ] Subtitle text is correct (not garbled)
- [ ] Subtitle timing is correct

#### Test 3.3: One-Click Playback - Japanese + English Subs
1. Navigate to episode with Japanese audio + English subs
2. Click JP/US flag button

**Checklist**:
- [ ] Audio is Japanese
- [ ] English subtitles are active
- [ ] All other playback features work

#### Test 3.4: Resume Playback
1. Start watching an episode, stop midway (e.g., 5 minutes in)
2. Navigate back to episode detail page
3. Click a flag button

**Checklist**:
- [ ] Video resumes from last position (not from start)
- [ ] Selected audio/subtitle tracks are applied
- [ ] Resume position is accurate (within 1-2 seconds)

#### Test 3.5: Switching Languages During Session
1. Start episode with one flag (e.g., German audio)
2. Return to detail page
3. Click different flag (e.g., Japanese + German subs)

**Checklist**:
- [ ] Video restarts with new language settings
- [ ] Previous progress is maintained
- [ ] New audio/subtitle tracks are correctly applied

---

### Phase 4: Edge Case Testing

#### Test 4.1: Files with Only One Audio Track
- [ ] Only one flag appears (no duplicate buttons)
- [ ] Clicking flag starts playback correctly
- [ ] No JavaScript errors

#### Test 4.2: Files with No Subtitles
- [ ] Only audio-only options appear (e.g., just "de" or "jp")
- [ ] No subtitle-related flags appear
- [ ] Playback works without subtitles

#### Test 4.3: Files with >3 Language Combinations
- [ ] All valid combinations appear as flag buttons
- [ ] UI doesn't overflow or break layout
- [ ] Each button works correctly
- [ ] Performance is acceptable (no lag)

#### Test 4.4: Files with Forced Subtitles
- [ ] Forced subtitles are excluded from main flag options
- [ ] Regular subtitles still appear as expected
- [ ] Forced subtitles don't create duplicate flags

#### Test 4.5: Non-Episode Content
Test with:
- Movies
- Music videos
- Live TV recordings

**Checklist**:
- [ ] Plugin works for movies (not just episodes)
- [ ] No errors for music content
- [ ] Gracefully handles unsupported content types

---

### Phase 5: Browser Compatibility

#### Test 5.1: Chrome/Chromium
- [ ] Flag buttons render correctly
- [ ] Playback integration works
- [ ] No console errors
- [ ] Hover effects work

#### Test 5.2: Firefox
- [ ] Flag buttons render correctly
- [ ] Playback integration works
- [ ] No console errors
- [ ] Hover effects work

#### Test 5.3: Edge
- [ ] Flag buttons render correctly
- [ ] Playback integration works

#### Test 5.4: Mobile Browsers (Optional)
- [ ] Buttons are tappable on mobile
- [ ] Layout is responsive
- [ ] Touch interactions work

---

### Phase 6: Performance & Stability

#### Test 6.1: Performance
- [ ] Page load time is not significantly impacted
- [ ] Flag buttons appear within 1 second of page load
- [ ] No memory leaks (check with browser dev tools)
- [ ] API response time is under 500ms

#### Test 6.2: Multiple Episodes
Navigate through multiple episodes rapidly:
- [ ] Buttons update correctly for each episode
- [ ] No stale data from previous episodes
- [ ] Observer cleans up properly (no duplicates)

#### Test 6.3: Error Handling
Simulate errors:
- [ ] Disconnect network → Should show error message
- [ ] Invalid item ID → Should fail gracefully
- [ ] API returns error → Should not crash page

---

## Common Issues & Solutions

### Issue 1: Flag Buttons Don't Appear
**Possible Causes**:
- JavaScript not loaded (check browser console)
- Page structure changed (check selectors)
- API endpoint not responding

**Debug Steps**:
1. Open browser console (F12)
2. Check for errors
3. Type: `window.languageSelector`
4. Type: `window.ApiClient`
5. Manually test API: `fetch('/Items/{id}/LanguageOptions')`

### Issue 2: Wrong Language Selected
**Possible Causes**:
- Stream indices mismatch
- Language detection incorrect

**Debug Steps**:
1. Check API response: verify `audioStreamIndex` and `subtitleStreamIndex`
2. Compare with actual file metadata (use MediaInfo tool)
3. Check Jellyfin logs for warnings

### Issue 3: Playback Doesn't Start
**Possible Causes**:
- Playback manager not available
- Invalid stream indices
- Permissions issue

**Debug Steps**:
1. Check console for "No playback manager found"
2. Test default play button works
3. Verify user has playback permissions

### Issue 4: Icons Not Loading
**Possible Causes**:
- Incorrect path to flag icons
- Icons not embedded in DLL

**Debug Steps**:
1. Check network tab (F12) for 404 errors
2. Verify icons are in: `/web/configurationpage?name=LanguageSelector/flags/`
3. Check plugin manifest includes all icons

---

## Log Files to Check

### Jellyfin Server Logs
Location: `%AppData%\Jellyfin\Server\log\`

Look for:
- Plugin loading messages
- API endpoint errors
- Media stream analysis warnings

### Browser Console Logs
Look for:
- JavaScript errors
- API fetch errors
- Playback manager warnings

---

## Success Criteria

✅ All Phase 1-3 tests pass  
✅ At least 2 browsers tested successfully  
✅ No critical bugs  
✅ Performance is acceptable  
✅ User experience matches AniWorld reference
