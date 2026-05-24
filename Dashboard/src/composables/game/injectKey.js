import { inject } from "vue";

export const GAME_VIEW_KEY = Symbol("gameView");

export function useGameViewInject() {
  const gameView = inject(GAME_VIEW_KEY);
  if (!gameView) {
    throw new Error(
      "useGameViewInject must be used within a GameView provider",
    );
  }
  return gameView;
}
