// Metro must also watch the portal's source: the app reuses its API types, translations and form validation
// (plain TypeScript modules, no browser components) instead of keeping copies in sync by hand.
const path = require("path");
const { getDefaultConfig } = require("expo/metro-config");

const config = getDefaultConfig(__dirname);
config.watchFolders = [...(config.watchFolders ?? []), path.resolve(__dirname, "../portal/src")];
module.exports = config;
