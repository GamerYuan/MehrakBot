<script setup>
import Card from "primevue/card";
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <div class="flex flex-col gap-4">
          <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
            Commendations
          </h2>
          <p class="text-zinc-300 leading-relaxed m-0">
            A special thanks to everyone who made Mehrak possible.
          </p>
        </div>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <div class="flex flex-col gap-8">
          <h3 class="text-2xl font-bold text-zinc-100 m-0 border-b border-white/10 pb-4">
            Big thanks to:
          </h3>

          <div class="flex flex-col gap-2">
            <h4 class="text-xl font-semibold text-zinc-100 m-0">Contributors</h4>
            <ul class="list-disc list-inside text-zinc-300 m-0">
              <li>GamerYuan</li>
            </ul>
          </div>

          <div class="flex flex-col gap-2">
            <h4 class="text-xl font-semibold text-zinc-100 m-0">HoYoverse Developers</h4>
            <p class="text-zinc-300 m-0">
              For creating fun games for all of us
            </p>
          </div>

          <div class="flex flex-col gap-2">
            <h4 class="text-xl font-semibold text-zinc-100 m-0">You!</h4>
            <p class="text-zinc-300 m-0">
              We couldn't have made it this far without your support
            </p>
          </div>
        </div>
      </template>
    </Card>

    <div class="mt-4 text-[11px] text-zinc-500 text-center leading-relaxed px-4">
      All assets belong to their respective owners. Genshin Impact, Honkai: Star Rail, Zenless Zone Zero and Honkai Impact 3rd are registered under the trademarks of Cognosphere Pte., Ltd. and miHoYo Co., Ltd. This website and the Discord Bot service is not associated with Cognosphere or miHoYo Co., Ltd. in any official capacity
    </div>
  </div>
</template>