
import { Component } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-student-dashboard',
  standalone: true,
  imports: [],
  templateUrl: './student-dashboard.component.html',
  styleUrl: './student-dashboard.component.css'
})
export class StudentDashboardComponent {
  constructor(private router: Router) {}

  logout(): void {
    sessionStorage.removeItem('accessToken');
    sessionStorage.removeItem('user');
    this.router.navigate(['/login']);
  }
}
