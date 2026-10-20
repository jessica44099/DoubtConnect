
import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-mentor-dashboard',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './mentor-dashboard.component.html',
  styleUrl: './mentor-dashboard.component.css'
})
export class MentorDashboardComponent {
  constructor(private router: Router) {}

  logout(): void {
    sessionStorage.removeItem('accessToken');
    this.router.navigate(['/login']);
  }
}
