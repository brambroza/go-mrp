/** Babel configuration: the Expo preset covers React Native, Expo Router and TypeScript. */
module.exports = function babelConfig(api) {
  api.cache(true);
  return { presets: ["babel-preset-expo"] };
};
