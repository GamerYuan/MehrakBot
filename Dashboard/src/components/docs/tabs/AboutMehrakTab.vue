<script setup>
import Card from "primevue/card";
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h2 class="text-3xl font-bold tracking-tight text-zinc-100 mb-2">
          About Mehrak
        </h2>
        <p class="text-zinc-300 leading-relaxed">
          Mehrak is your all-in-one HoYoverse games toolbox. Mehrak is committed to provide the best user experience to all our users through convenient commands and powerful utilities
        </p>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-6">
          Why Mehrak?
        </h3>

        <div class="flex flex-col gap-6">
          <div>
            <h4 class="text-xl font-bold text-zinc-100 mb-2">User-First Approach</h4>
            <p class="text-zinc-300 leading-relaxed">
              Users are our main concern when developing Mehrak. The development of Mehrak thus far has been based on user feedback. We are committed to improve our services through user feedback and suggestions
            </p>
          </div>

          <div class="pt-4 border-t border-white/10">
            <h4 class="text-xl font-bold text-zinc-100 mb-2">Security</h4>
            <p class="text-zinc-300 leading-relaxed">
              Mehrak is one of the only few HoYoverse games Discord bot that offers zero-knowledge encryption and security for your cookies information. Your information is safe with us, and we're committed to providing the best security measures for our users
            </p>
          </div>

          <div class="pt-4 border-t border-white/10">
            <h4 class="text-xl font-bold text-zinc-100 mb-2">Free</h4>
            <p class="text-zinc-300 leading-relaxed">
              All Mehrak services are provided for free. We are committed to keeping all our services free in the future, with no paywalls or premium benefits
            </p>
          </div>

          <div class="pt-4 border-t border-white/10">
            <h4 class="text-xl font-bold text-zinc-100 mb-2">Features</h4>
            <p class="text-zinc-300 leading-relaxed">
              Mehrak is still early in its development phase, and we're still lacking in features. Our vision is to be the most fully featured HoYoverse games Discord bot that covers all HoYoverse titles, in the past and in the future. You can be part of our journey by using Mehrak services
            </p>
          </div>

          <div class="pt-4 border-t border-white/10">
            <h4 class="text-xl font-bold text-zinc-100 mb-2">Open Source</h4>
            <p class="text-zinc-300 leading-relaxed">
              Mehrak is fully open-source. This allows you to see how your data is being used. You may also audit the source code at any given time and voice your concerns about our implementation approach at any time
            </p>
          </div>
        </div>
      </template>
    </Card>

    <Card class="bg-white/5 border border-white/10 rounded-2xl">
      <template #content>
        <h3 class="text-2xl font-semibold tracking-tight text-zinc-100 mb-4">
          Self-Hosting
        </h3>
        <p class="text-zinc-300 leading-relaxed">
          We will not provide any instructions to self-host Mehrak. However, with the source code available, with enough technical expertise you can technically self-host Mehrak if you wish to. You must comply to our
          <a
            href="https://github.com/GamerYuan/MehrakBot/blob/master/LICENSE.txt"
            target="_blank"
            rel="noopener noreferrer"
            class="text-emerald-400 hover:text-emerald-300 transition-colors"
            >license</a
          >
          to self-host. Self-hosted Mehrak instances have no affiliation with the official Mehrak Bot. The Mehrak developers do not have control over what modifications have been made with self-hosted Mehrak instances
        </p>
      </template>
    </Card>
  </div>
</template>
