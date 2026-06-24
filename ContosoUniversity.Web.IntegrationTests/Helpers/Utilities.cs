using System;
using ContosoUniversity.Data;
using ContosoUniversity.Data.DbContexts;
using ContosoUniversity.Data.Entities;

namespace ContosoUniversity.Web.IntegrationTests
{
    public static class Utilities
    {
        public static void InitializeDbForTest(ApplicationContext db)
        {
            // id = 1 (Person TPH)
            var student = new Student { FirstMidName = "Anna", LastName = "Garland", EnrollmentDate = DateTime.Now };
            db.Students.Add(student);
            db.SaveChanges();

            // id = 2 (Person TPH) — Instructor with its 1:1 OfficeAssignment.
            // OfficeAssignment.InstructorID is the PK/FK, so the Instructor must exist first.
            // Saving them together lets EF order the inserts correctly; real SQL Server enforces
            // the FK (the in-memory provider did not, which masked this bug).
            var instructor = new Instructor
            {
                LastName = "Smith",
                FirstMidName = "John",
                HireDate = DateTime.Now,
                OfficeAssignment = new OfficeAssignment { Location = "A" }
            };
            db.Instructors.Add(instructor);
            db.SaveChanges();

            // id = 1
            var department = new Department
            {
                Administrator = instructor,
                Name = "Engineering",
                Budget = 100
            };
            db.Departments.Add(department);
            db.SaveChanges();

            // id = 1
            var course = new Course
            {
                Department = department,
                Title = "Intro to Engineering",
                Credits = 3
            };
            db.Courses.Add(course);
            db.SaveChanges();

            var courseAssignment = new CourseAssignment { CourseID = course.ID, InstructorID = instructor.ID };
            db.CourseAssignments.Add(courseAssignment);
            db.SaveChanges();
        }
    }

}