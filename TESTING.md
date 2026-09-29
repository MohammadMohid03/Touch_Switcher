# Testing checklist

## Application switching

- [ ] Chrome → VS Code
- [ ] VS Code → Chrome
- [ ] Chrome → Spotify
- [ ] Multiple Chrome windows (Application mode stays on Chrome as one entry)
- [ ] Multiple VS Code windows (same)
- [ ] Window mode cycles individual Chrome / VS Code windows
- [ ] Minimized applications restore and activate
- [ ] File Explorer
- [ ] Windows Settings
- [ ] Notepad
- [ ] Electron apps
- [ ] UWP / WinUI apps
- [ ] Repeat swipes: ← → ← → → ← with no overlay

## Fullscreen and exclusions

- [ ] Fullscreen game / video does not switch when the option is on
- [ ] Excluded `Game.exe` is never activated
- [ ] Remote Desktop / VM can be excluded

## Privilege

- [ ] Switching among same-integrity apps works without administrator
- [ ] Elevated target may fail without elevation (documented limitation)

## Gesture rejection

- [ ] 2-finger scroll does not switch
- [ ] 3-finger vertical swipe does not switch
- [ ] 3-finger tap does not switch
- [ ] 4-finger gesture does not switch
- [ ] Fast swipe switches once
- [ ] Slow swipe still works if distance is met (increase timeout if needed)
- [ ] Short swipe below threshold does not switch
- [ ] Accidental palm / two-finger rest does not switch

## Shell UI

- [ ] No Alt+Tab overlay
- [ ] No custom HUD
- [ ] No application name bar
