import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { WarmupService } from './core/warmup.service';
import { ToastContainer } from './shared/toast-container';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastContainer],
  template: `<router-outlet /><app-toast-container />`
})
export class App {
  constructor() {
    // Wake sleeping free-tier services as early as possible (even before sign-in).
    inject(WarmupService).start();
  }
}
