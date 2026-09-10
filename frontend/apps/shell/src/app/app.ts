import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { NavigationShell } from './navigation/navigation-shell';

@Component({
  imports: [NavigationShell, RouterModule],
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected title = 'shell';
}
